import Parser from 'tree-sitter';
import { isLanguageAvailable, loadParser, loadLanguage } from '../tree-sitter/parser-loader.js';
import { LANGUAGE_QUERIES } from './tree-sitter-queries.js';
import { generateId } from '../../lib/utils.js';
import { getLanguageFromFilename, isVerboseIngestionEnabled, yieldToEventLoop } from './utils.js';
import { SupportedLanguages } from '../../config/supported-languages.js';
import { getTreeSitterBufferSize } from './constants.js';
import { loadImportConfigs } from './language-config.js';
import { buildSuffixIndex } from './resolvers/index.js';
import { callRouters } from './call-routing.js';
import { importResolvers, namedBindingExtractors, preprocessImportPath } from './import-resolution.js';
const isDev = process.env.NODE_ENV === 'development';
/**
 * Check if a file path is directly inside a package directory identified by its suffix.
 * Used by the symbol resolver for Go and C# directory-level import matching.
 */
export function isFileInPackageDir(filePath, dirSuffix) {
    // Prepend '/' so paths like "internal/auth/service.go" match suffix "/internal/auth/"
    const normalized = '/' + filePath.replace(/\\/g, '/');
    if (!normalized.includes(dirSuffix))
        return false;
    const afterDir = normalized.substring(normalized.indexOf(dirSuffix) + dirSuffix.length);
    return !afterDir.includes('/');
}
export function buildImportResolutionContext(allPaths) {
    const allFileList = allPaths;
    const normalizedFileList = allFileList.map(p => p.replace(/\\/g, '/'));
    const allFilePaths = new Set(allFileList);
    const index = buildSuffixIndex(normalizedFileList, allFileList);
    return { allFilePaths, allFileList, normalizedFileList, index, resolveCache: new Map() };
}
// Config loaders extracted to ./language-config.ts (Phase 2 refactor)
// Resolver dispatch tables are in ./import-resolution.ts — imported above
/** Create IMPORTS edge helpers that share a resolved-count tracker. */
function createImportEdgeHelpers(graph, importMap) {
    let totalImportsResolved = 0;
    const addImportGraphEdge = (filePath, resolvedPath) => {
        const sourceId = generateId('File', filePath);
        const targetId = generateId('File', resolvedPath);
        const relId = generateId('IMPORTS', `${filePath}->${resolvedPath}`);
        totalImportsResolved++;
        graph.addRelationship({ id: relId, sourceId, targetId, type: 'IMPORTS', confidence: 1.0, reason: '' });
    };
    const addImportEdge = (filePath, resolvedPath) => {
        addImportGraphEdge(filePath, resolvedPath);
        if (!importMap.has(filePath))
            importMap.set(filePath, new Set());
        importMap.get(filePath).add(resolvedPath);
    };
    return { addImportEdge, addImportGraphEdge, getResolvedCount: () => totalImportsResolved };
}
/**
 * Group Swift files by target for implicit module visibility.
 *
 * If SwiftPackageConfig is available, use SPM target → directory mappings.
 * Otherwise, group all Swift files under a single "default" target
 * (assumes a single-module Xcode project).
 */
function groupSwiftFilesByTarget(swiftFiles, swiftPackageConfig) {
    const groups = new Map();
    if (swiftPackageConfig && swiftPackageConfig.targets.size > 0) {
        for (const file of swiftFiles) {
            const normalized = file.replace(/\\/g, '/');
            let assigned = false;
            for (const [targetName, targetDir] of swiftPackageConfig.targets) {
                const dirPrefix = targetDir + '/';
                const idx = normalized.indexOf(dirPrefix);
                if (idx === 0 || (idx > 0 && normalized[idx - 1] === '/')) {
                    if (!groups.has(targetName))
                        groups.set(targetName, []);
                    groups.get(targetName).push(file);
                    assigned = true;
                    break;
                }
            }
            if (!assigned) {
                if (!groups.has('__default__'))
                    groups.set('__default__', []);
                groups.get('__default__').push(file);
            }
        }
    }
    else {
        groups.set('__default__', [...swiftFiles]);
    }
    return groups;
}
/**
 * Add implicit IMPORTS edges between all Swift files in the same module/target.
 * Swift has no file-level imports — all files in a module see each other.
 */
function addSwiftImplicitImports(files, swiftPackageConfig, importMap, addImportEdge, logSuffix = '') {
    const paths = typeof files[0] === 'string'
        ? files
        : files.map(f => f.path);
    const swiftFiles = paths
        .filter(f => getLanguageFromFilename(f) === SupportedLanguages.Swift);
    if (swiftFiles.length <= 1)
        return;
    const targetGroups = groupSwiftFilesByTarget(swiftFiles, swiftPackageConfig);
    for (const group of targetGroups.values()) {
        for (const srcFile of group) {
            const existing = importMap.get(srcFile);
            for (const otherFile of group) {
                if (srcFile === otherFile)
                    continue;
                if (existing?.has(otherFile))
                    continue;
                addImportEdge(srcFile, otherFile);
            }
        }
    }
    if (isDev) {
        console.log(`📊 Swift: ${swiftFiles.length} files in ${targetGroups.size} target group(s), implicit imports added${logSuffix}`);
    }
}
/**
 * Apply an ImportResult: emit graph edges and update ImportMap/PackageMap.
 * If namedBindings are provided and the import resolves to a single file,
 * also populate the NamedImportMap for precise Tier 2a resolution.
 * Bindings tagged with `isModuleAlias` are routed to moduleAliasMap instead.
 */
function applyImportResult(result, filePath, importMap, packageMap, addImportEdge, addImportGraphEdge, namedBindings, namedImportMap, moduleAliasMap) {
    if (!result)
        return;
    if (result.kind === 'package' && packageMap) {
        // Store directory suffix in PackageMap (skip ImportMap expansion)
        for (const resolvedFile of result.files) {
            addImportGraphEdge(filePath, resolvedFile);
        }
        if (!packageMap.has(filePath))
            packageMap.set(filePath, new Set());
        packageMap.get(filePath).add(result.dirSuffix);
    }
    else {
        // 'files' kind, or 'package' without PackageMap — use ImportMap directly
        const files = result.files;
        for (const resolvedFile of files) {
            addImportEdge(filePath, resolvedFile);
        }
        // Route module aliases (import X as Y) directly to moduleAliasMap.
        // These are module-level aliases, not symbol bindings — they don't belong in namedImportMap.
        if (namedBindings && moduleAliasMap && files.length === 1) {
            const resolvedFile = files[0];
            for (const binding of namedBindings) {
                if (!binding.isModuleAlias)
                    continue;
                let aliasMap = moduleAliasMap.get(filePath);
                if (!aliasMap) {
                    aliasMap = new Map();
                    moduleAliasMap.set(filePath, aliasMap);
                }
                aliasMap.set(binding.local, resolvedFile);
            }
        }
        // Record named bindings for precise Tier 2a resolution.
        // If the same local name is imported from multiple files (e.g., Java static imports
        // of overloaded methods), remove the entry so resolution falls through to Tier 2a
        // import-scoped which sees all candidates and can apply arity narrowing.
        if (namedBindings && namedImportMap) {
            if (!namedImportMap.has(filePath))
                namedImportMap.set(filePath, new Map());
            const fileBindings = namedImportMap.get(filePath);
            if (files.length === 1) {
                const resolvedFile = files[0];
                for (const binding of namedBindings) {
                    if (binding.isModuleAlias)
                        continue; // already routed to moduleAliasMap
                    const existing = fileBindings.get(binding.local);
                    if (existing && existing.sourcePath !== resolvedFile) {
                        fileBindings.delete(binding.local);
                    }
                    else {
                        fileBindings.set(binding.local, { sourcePath: resolvedFile, exportedName: binding.exported });
                    }
                }
            }
            else {
                // Multi-file resolution (e.g., Rust `use crate::models::{User, Repo}`).
                // Match each binding to a resolved file by comparing the lowercase binding name
                // to the file's basename (without extension). If no match, skip the binding.
                for (const binding of namedBindings) {
                    if (binding.isModuleAlias)
                        continue;
                    const lowerName = binding.exported.toLowerCase();
                    const matchedFile = files.find(f => {
                        const base = f.replace(/\\/g, '/').split('/').pop() ?? '';
                        const nameWithoutExt = base.substring(0, base.lastIndexOf('.')).toLowerCase();
                        return nameWithoutExt === lowerName;
                    });
                    if (matchedFile) {
                        const existing = fileBindings.get(binding.local);
                        if (existing && existing.sourcePath !== matchedFile) {
                            fileBindings.delete(binding.local);
                        }
                        else {
                            fileBindings.set(binding.local, { sourcePath: matchedFile, exportedName: binding.exported });
                        }
                    }
                }
            }
        }
    }
}
// ============================================================================
// MAIN IMPORT PROCESSOR
// ============================================================================
export const processImports = async (graph, files, astCache, ctx, onProgress, repoRoot, allPaths) => {
    const importMap = ctx.importMap;
    const packageMap = ctx.packageMap;
    const namedImportMap = ctx.namedImportMap;
    const moduleAliasMap = ctx.moduleAliasMap;
    // Use allPaths (full repo) when available for cross-chunk resolution, else fall back to chunk files
    const allFileList = allPaths ?? files.map(f => f.path);
    const allFilePaths = new Set(allFileList);
    const parser = await loadParser();
    const logSkipped = isVerboseIngestionEnabled();
    const skippedByLang = logSkipped ? new Map() : null;
    const resolveCache = new Map();
    // Pre-compute normalized file list once (forward slashes)
    const normalizedFileList = allFileList.map(p => p.replace(/\\/g, '/'));
    // Build suffix index for O(1) lookups
    const index = buildSuffixIndex(normalizedFileList, allFileList);
    // Track import statistics
    let totalImportsFound = 0;
    // Load language-specific configs once before the file loop
    const configs = await loadImportConfigs(repoRoot || '');
    const resolveCtx = { allFilePaths, allFileList, normalizedFileList, index, resolveCache, configs };
    const { addImportEdge, addImportGraphEdge, getResolvedCount } = createImportEdgeHelpers(graph, importMap);
    for (let i = 0; i < files.length; i++) {
        const file = files[i];
        onProgress?.(i + 1, files.length);
        if (i % 20 === 0)
            await yieldToEventLoop();
        // 1. Check language support first
        const language = getLanguageFromFilename(file.path);
        if (!language)
            continue;
        if (!isLanguageAvailable(language)) {
            if (skippedByLang) {
                skippedByLang.set(language, (skippedByLang.get(language) ?? 0) + 1);
            }
            continue;
        }
        const queryStr = LANGUAGE_QUERIES[language];
        if (!queryStr)
            continue;
        // 2. ALWAYS load the language before querying (parser is stateful)
        await loadLanguage(language, file.path);
        // 3. Get AST (Try Cache First)
        let tree = astCache.get(file.path);
        let wasReparsed = false;
        if (!tree) {
            try {
                tree = parser.parse(file.content, undefined, { bufferSize: getTreeSitterBufferSize(file.content.length) });
            }
            catch (parseError) {
                continue;
            }
            wasReparsed = true;
            // Cache re-parsed tree so call/heritage phases get hits
            astCache.set(file.path, tree);
        }
        let query;
        let matches;
        try {
            const lang = parser.getLanguage();
            query = new Parser.Query(lang, queryStr);
            matches = query.matches(tree.rootNode);
        }
        catch (queryError) {
            if (isDev) {
                console.group(`🔴 Query Error: ${file.path}`);
                console.log('Language:', language);
                console.log('Query (first 200 chars):', queryStr.substring(0, 200) + '...');
                console.log('Error:', queryError?.message || queryError);
                console.log('File content (first 300 chars):', file.content.substring(0, 300));
                console.log('AST root type:', tree.rootNode?.type);
                console.log('AST has errors:', tree.rootNode?.hasError);
                console.groupEnd();
            }
            if (wasReparsed)
                tree.delete?.();
            continue;
        }
        matches.forEach(match => {
            const captureMap = {};
            match.captures.forEach(c => captureMap[c.name] = c.node);
            if (captureMap['import']) {
                const sourceNode = captureMap['import.source'];
                if (!sourceNode) {
                    if (isDev) {
                        console.log(`⚠️ Import captured but no source node in ${file.path}`);
                    }
                    return;
                }
                const rawImportPath = preprocessImportPath(sourceNode.text, captureMap['import'], language);
                if (!rawImportPath)
                    return;
                totalImportsFound++;
                const result = importResolvers[language](rawImportPath, file.path, resolveCtx);
                const extractor = namedBindingExtractors[language];
                const bindings = namedImportMap && extractor ? extractor(captureMap['import']) : undefined;
                applyImportResult(result, file.path, importMap, packageMap, addImportEdge, addImportGraphEdge, bindings, namedImportMap, moduleAliasMap);
            }
            // ---- Language-specific call-as-import routing (Ruby require, etc.) ----
            if (captureMap['call']) {
                const callNameNode = captureMap['call.name'];
                if (callNameNode) {
                    const callRouter = callRouters[language];
                    const routed = callRouter(callNameNode.text, captureMap['call']);
                    if (routed && routed.kind === 'import') {
                        totalImportsFound++;
                        const result = importResolvers[language](routed.importPath, file.path, resolveCtx);
                        applyImportResult(result, file.path, importMap, packageMap, addImportEdge, addImportGraphEdge);
                    }
                }
            }
        });
        // Tree is now owned by the LRU cache — no manual delete needed
    }
    addSwiftImplicitImports(allFileList, configs.swiftPackageConfig, importMap, addImportEdge);
    if (skippedByLang && skippedByLang.size > 0) {
        for (const [lang, count] of skippedByLang.entries()) {
            console.warn(`[ingestion] Skipped ${count} ${lang} file(s) in import processing — ${lang} parser not available.`);
        }
    }
    if (isDev) {
        console.log(`📊 Import processing complete: ${getResolvedCount()}/${totalImportsFound} imports resolved to graph edges`);
    }
};
// ============================================================================
// FAST PATH: Resolve pre-extracted imports (no parsing needed)
// ============================================================================
export const processImportsFromExtracted = async (graph, files, extractedImports, ctx, onProgress, repoRoot, prebuiltCtx) => {
    const importMap = ctx.importMap;
    const packageMap = ctx.packageMap;
    const namedImportMap = ctx.namedImportMap;
    const moduleAliasMap = ctx.moduleAliasMap;
    const importCtx = prebuiltCtx ?? buildImportResolutionContext(files.map(f => f.path));
    const { allFilePaths, allFileList, normalizedFileList, index, resolveCache } = importCtx;
    let totalImportsFound = 0;
    const configs = await loadImportConfigs(repoRoot || '');
    const resolveCtx = { allFilePaths, allFileList, normalizedFileList, index, resolveCache, configs };
    const { addImportEdge, addImportGraphEdge, getResolvedCount } = createImportEdgeHelpers(graph, importMap);
    // Group by file for progress reporting (users see file count, not import count)
    const importsByFile = new Map();
    for (const imp of extractedImports) {
        let list = importsByFile.get(imp.filePath);
        if (!list) {
            list = [];
            importsByFile.set(imp.filePath, list);
        }
        list.push(imp);
    }
    const totalFiles = importsByFile.size;
    let filesProcessed = 0;
    for (const [filePath, fileImports] of importsByFile) {
        filesProcessed++;
        if (filesProcessed % 100 === 0) {
            onProgress?.(filesProcessed, totalFiles);
            await yieldToEventLoop();
        }
        for (const imp of fileImports) {
            totalImportsFound++;
            const result = importResolvers[imp.language](imp.rawImportPath, filePath, resolveCtx);
            applyImportResult(result, filePath, importMap, packageMap, addImportEdge, addImportGraphEdge, imp.namedBindings, namedImportMap, moduleAliasMap);
        }
    }
    onProgress?.(totalFiles, totalFiles);
    addSwiftImplicitImports(files, configs.swiftPackageConfig, importMap, addImportEdge, ' (fast path)');
    if (isDev) {
        console.log(`📊 Import processing (fast path): ${getResolvedCount()}/${totalImportsFound} imports resolved to graph edges`);
    }
};
