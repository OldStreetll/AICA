import { parentPort } from 'node:worker_threads';
import Parser from 'tree-sitter';
import JavaScript from 'tree-sitter-javascript';
import TypeScript from 'tree-sitter-typescript';
import Python from 'tree-sitter-python';
import Java from 'tree-sitter-java';
import C from 'tree-sitter-c';
import CPP from 'tree-sitter-cpp';
import CSharp from 'tree-sitter-c-sharp';
import Go from 'tree-sitter-go';
import Rust from 'tree-sitter-rust';
import PHP from 'tree-sitter-php';
import Ruby from 'tree-sitter-ruby';
import { createRequire } from 'node:module';
import { SupportedLanguages } from '../../../config/supported-languages.js';
import { LANGUAGE_QUERIES } from '../tree-sitter-queries.js';
import { getTreeSitterBufferSize, TREE_SITTER_MAX_BUFFER } from '../constants.js';
// tree-sitter-swift is an optionalDependency — may not be installed
const _require = createRequire(import.meta.url);
let Swift = null;
try {
    Swift = _require('tree-sitter-swift');
}
catch { }
// tree-sitter-kotlin is an optionalDependency — may not be installed
let Kotlin = null;
try {
    Kotlin = _require('tree-sitter-kotlin');
}
catch { }
import { getLanguageFromFilename, FUNCTION_NODE_TYPES, extractFunctionName, isBuiltInOrNoise, getDefinitionNodeFromCaptures, findEnclosingClassId, getLabelFromCaptures, extractMethodSignature, countCallArguments, inferCallForm, extractReceiverName, extractReceiverNode, extractMixedChain, } from '../utils.js';
import { buildTypeEnv } from '../type-env.js';
import { isNodeExported } from '../export-detection.js';
import { detectFrameworkFromAST } from '../framework-detection.js';
import { typeConfigs } from '../type-extractors/index.js';
import { generateId } from '../../../lib/utils.js';
import { namedBindingExtractors, preprocessImportPath } from '../import-resolution.js';
import { callRouters } from '../call-routing.js';
import { extractPropertyDeclaredType } from '../type-extractors/shared.js';
// ============================================================================
// Worker-local parser + language map
// ============================================================================
const parser = new Parser();
const languageMap = {
    [SupportedLanguages.JavaScript]: JavaScript,
    [SupportedLanguages.TypeScript]: TypeScript.typescript,
    [`${SupportedLanguages.TypeScript}:tsx`]: TypeScript.tsx,
    [SupportedLanguages.Python]: Python,
    [SupportedLanguages.Java]: Java,
    [SupportedLanguages.C]: C,
    [SupportedLanguages.CPlusPlus]: CPP,
    [SupportedLanguages.CSharp]: CSharp,
    [SupportedLanguages.Go]: Go,
    [SupportedLanguages.Rust]: Rust,
    ...(Kotlin ? { [SupportedLanguages.Kotlin]: Kotlin } : {}),
    [SupportedLanguages.PHP]: PHP.php_only,
    [SupportedLanguages.Ruby]: Ruby,
    ...(Swift ? { [SupportedLanguages.Swift]: Swift } : {}),
};
/**
 * Check if a language grammar is available in this worker.
 * Duplicated from parser-loader.ts because workers can't import from the main thread.
 * Extra filePath parameter needed to distinguish .tsx from .ts (different grammars
 * under the same SupportedLanguages.TypeScript key).
 */
const isLanguageAvailable = (language, filePath) => {
    const key = language === SupportedLanguages.TypeScript && filePath.endsWith('.tsx')
        ? `${language}:tsx`
        : language;
    return key in languageMap && languageMap[key] != null;
};
const setLanguage = (language, filePath) => {
    const key = language === SupportedLanguages.TypeScript && filePath.endsWith('.tsx')
        ? `${language}:tsx`
        : language;
    const lang = languageMap[key];
    if (!lang)
        throw new Error(`Unsupported language: ${language}`);
    parser.setLanguage(lang);
};
// isNodeExported imported from ../export-detection.js (shared module)
// ============================================================================
// Enclosing function detection (for call extraction)
// ============================================================================
/** Walk up AST to find enclosing function, return its generateId or null for top-level */
const findEnclosingFunctionId = (node, filePath) => {
    let current = node.parent;
    while (current) {
        if (FUNCTION_NODE_TYPES.has(current.type)) {
            const { funcName, label } = extractFunctionName(current);
            if (funcName) {
                return generateId(label, `${filePath}:${funcName}`);
            }
        }
        current = current.parent;
    }
    return null;
};
// Label detection moved to shared getLabelFromCaptures in utils.ts
// DEFINITION_CAPTURE_KEYS and getDefinitionNodeFromCaptures imported from ../utils.js
// ============================================================================
// Process a batch of files
// ============================================================================
const processBatch = (files, onProgress) => {
    const result = {
        nodes: [],
        relationships: [],
        symbols: [],
        imports: [],
        calls: [],
        assignments: [],
        heritage: [],
        routes: [],
        fetchCalls: [],
        decoratorRoutes: [],
        toolDefs: [],
        constructorBindings: [],
        typeEnvBindings: [],
        skippedLanguages: {},
        fileCount: 0,
    };
    // Group by language to minimize setLanguage calls
    const byLanguage = new Map();
    for (const file of files) {
        const lang = getLanguageFromFilename(file.path);
        if (!lang)
            continue;
        let list = byLanguage.get(lang);
        if (!list) {
            list = [];
            byLanguage.set(lang, list);
        }
        list.push(file);
    }
    let totalProcessed = 0;
    let lastReported = 0;
    const PROGRESS_INTERVAL = 100; // report every 100 files
    const onFileProcessed = onProgress ? () => {
        totalProcessed++;
        if (totalProcessed - lastReported >= PROGRESS_INTERVAL) {
            lastReported = totalProcessed;
            onProgress(totalProcessed);
        }
    } : undefined;
    for (const [language, langFiles] of byLanguage) {
        const queryString = LANGUAGE_QUERIES[language];
        if (!queryString)
            continue;
        // Track if we need to handle tsx separately
        const tsxFiles = [];
        const regularFiles = [];
        if (language === SupportedLanguages.TypeScript) {
            for (const f of langFiles) {
                if (f.path.endsWith('.tsx')) {
                    tsxFiles.push(f);
                }
                else {
                    regularFiles.push(f);
                }
            }
        }
        else {
            regularFiles.push(...langFiles);
        }
        // Process regular files for this language
        if (regularFiles.length > 0) {
            if (isLanguageAvailable(language, regularFiles[0].path)) {
                try {
                    setLanguage(language, regularFiles[0].path);
                    processFileGroup(regularFiles, language, queryString, result, onFileProcessed);
                }
                catch {
                    // parser unavailable — skip this language group
                }
            }
            else {
                result.skippedLanguages[language] = (result.skippedLanguages[language] || 0) + regularFiles.length;
            }
        }
        // Process tsx files separately (different grammar)
        if (tsxFiles.length > 0) {
            if (isLanguageAvailable(language, tsxFiles[0].path)) {
                try {
                    setLanguage(language, tsxFiles[0].path);
                    processFileGroup(tsxFiles, language, queryString, result, onFileProcessed);
                }
                catch {
                    // parser unavailable — skip this language group
                }
            }
            else {
                result.skippedLanguages[language] = (result.skippedLanguages[language] || 0) + tsxFiles.length;
            }
        }
    }
    return result;
};
// ============================================================================
// PHP Eloquent metadata extraction
// ============================================================================
/** Eloquent model properties whose array values are worth indexing */
const ELOQUENT_ARRAY_PROPS = new Set(['fillable', 'casts', 'hidden', 'guarded', 'with', 'appends']);
/** Eloquent relationship method names */
const ELOQUENT_RELATIONS = new Set([
    'hasMany', 'hasOne', 'belongsTo', 'belongsToMany',
    'morphTo', 'morphMany', 'morphOne', 'morphToMany', 'morphedByMany',
    'hasManyThrough', 'hasOneThrough',
]);
function findDescendant(node, type) {
    if (node.type === type)
        return node;
    for (const child of (node.children ?? [])) {
        const found = findDescendant(child, type);
        if (found)
            return found;
    }
    return null;
}
function extractStringContent(node) {
    if (!node)
        return null;
    const content = node.children?.find((c) => c.type === 'string_content');
    if (content)
        return content.text;
    if (node.type === 'string_content')
        return node.text;
    return null;
}
/**
 * For a PHP property_declaration node, extract array values as a description string.
 * Returns null if not an Eloquent model property or no array values found.
 */
function extractPhpPropertyDescription(propName, propDeclNode) {
    if (!ELOQUENT_ARRAY_PROPS.has(propName))
        return null;
    const arrayNode = findDescendant(propDeclNode, 'array_creation_expression');
    if (!arrayNode)
        return null;
    const items = [];
    for (const child of (arrayNode.children ?? [])) {
        if (child.type !== 'array_element_initializer')
            continue;
        const children = child.children ?? [];
        const arrowIdx = children.findIndex((c) => c.type === '=>');
        if (arrowIdx !== -1) {
            // key => value pair (used in $casts)
            const key = extractStringContent(children[arrowIdx - 1]);
            const val = extractStringContent(children[arrowIdx + 1]);
            if (key && val)
                items.push(`${key}:${val}`);
        }
        else {
            // Simple value (used in $fillable, $hidden, etc.)
            const val = extractStringContent(children[0]);
            if (val)
                items.push(val);
        }
    }
    return items.length > 0 ? items.join(', ') : null;
}
/**
 * For a PHP method_declaration node, detect if it defines an Eloquent relationship.
 * Returns description like "hasMany(Post)" or null.
 */
function extractEloquentRelationDescription(methodNode) {
    function findRelationCall(node) {
        if (node.type === 'member_call_expression') {
            const children = node.children ?? [];
            const objectNode = children.find((c) => c.type === 'variable_name' && c.text === '$this');
            const nameNode = children.find((c) => c.type === 'name');
            if (objectNode && nameNode && ELOQUENT_RELATIONS.has(nameNode.text))
                return node;
        }
        for (const child of (node.children ?? [])) {
            const found = findRelationCall(child);
            if (found)
                return found;
        }
        return null;
    }
    const callNode = findRelationCall(methodNode);
    if (!callNode)
        return null;
    const relType = callNode.children?.find((c) => c.type === 'name')?.text;
    const argsNode = callNode.children?.find((c) => c.type === 'arguments');
    let targetModel = null;
    if (argsNode) {
        const firstArg = argsNode.children?.find((c) => c.type === 'argument');
        if (firstArg) {
            const classConstant = firstArg.children?.find((c) => c.type === 'class_constant_access_expression');
            if (classConstant) {
                targetModel = classConstant.children?.find((c) => c.type === 'name')?.text ?? null;
            }
        }
    }
    if (relType && targetModel)
        return `${relType}(${targetModel})`;
    if (relType)
        return relType;
    return null;
}
const ROUTE_HTTP_METHODS = new Set([
    'get', 'post', 'put', 'patch', 'delete', 'options', 'any', 'match',
]);
const ROUTE_RESOURCE_METHODS = new Set(['resource', 'apiResource']);
// Express/Hono method names that register routes
const EXPRESS_ROUTE_METHODS = new Set(['get', 'post', 'put', 'delete', 'patch', 'all', 'use', 'route']);
// HTTP client methods that are ONLY used by clients, not Express route registration.
// Methods like get/post/put/delete/patch overlap with Express — those are captured by
// the express_route handler as route definitions, not consumers. The fetch() global
// function is captured separately by the route.fetch query.
const HTTP_CLIENT_ONLY_METHODS = new Set(['head', 'options', 'request', 'ajax']);
// Decorator names that indicate HTTP route handlers (NestJS, Flask, FastAPI, Spring)
const ROUTE_DECORATOR_NAMES = new Set([
    'Get', 'Post', 'Put', 'Delete', 'Patch', 'Route',
    'get', 'post', 'put', 'delete', 'patch', 'route',
    'RequestMapping', 'GetMapping', 'PostMapping', 'PutMapping', 'DeleteMapping',
]);
const RESOURCE_ACTIONS = ['index', 'create', 'store', 'show', 'edit', 'update', 'destroy'];
const API_RESOURCE_ACTIONS = ['index', 'store', 'show', 'update', 'destroy'];
/** Check if node is a scoped_call_expression with object 'Route' */
function isRouteStaticCall(node) {
    if (node.type !== 'scoped_call_expression')
        return false;
    const obj = node.childForFieldName?.('object') ?? node.children?.[0];
    return obj?.text === 'Route';
}
/** Get the method name from a scoped_call_expression or member_call_expression */
function getCallMethodName(node) {
    const nameNode = node.childForFieldName?.('name') ??
        node.children?.find((c) => c.type === 'name');
    return nameNode?.text ?? null;
}
/** Get the arguments node from a call expression */
function getArguments(node) {
    return node.children?.find((c) => c.type === 'arguments') ?? null;
}
/** Find the closure body inside arguments */
function findClosureBody(argsNode) {
    if (!argsNode)
        return null;
    for (const child of argsNode.children ?? []) {
        if (child.type === 'argument') {
            for (const inner of child.children ?? []) {
                if (inner.type === 'anonymous_function' ||
                    inner.type === 'arrow_function') {
                    return inner.childForFieldName?.('body') ??
                        inner.children?.find((c) => c.type === 'compound_statement');
                }
            }
        }
        if (child.type === 'anonymous_function' ||
            child.type === 'arrow_function') {
            return child.childForFieldName?.('body') ??
                child.children?.find((c) => c.type === 'compound_statement');
        }
    }
    return null;
}
/** Extract first string argument from arguments node */
function extractFirstStringArg(argsNode) {
    if (!argsNode)
        return null;
    for (const child of argsNode.children ?? []) {
        const target = child.type === 'argument' ? child.children?.[0] : child;
        if (!target)
            continue;
        if (target.type === 'string' || target.type === 'encapsed_string') {
            return extractStringContent(target);
        }
    }
    return null;
}
/** Extract middleware from arguments — handles string or array */
function extractMiddlewareArg(argsNode) {
    if (!argsNode)
        return [];
    for (const child of argsNode.children ?? []) {
        const target = child.type === 'argument' ? child.children?.[0] : child;
        if (!target)
            continue;
        if (target.type === 'string' || target.type === 'encapsed_string') {
            const val = extractStringContent(target);
            return val ? [val] : [];
        }
        if (target.type === 'array_creation_expression') {
            const items = [];
            for (const el of target.children ?? []) {
                if (el.type === 'array_element_initializer') {
                    const str = el.children?.find((c) => c.type === 'string' || c.type === 'encapsed_string');
                    const val = str ? extractStringContent(str) : null;
                    if (val)
                        items.push(val);
                }
            }
            return items;
        }
    }
    return [];
}
/** Extract Controller::class from arguments */
function extractClassArg(argsNode) {
    if (!argsNode)
        return null;
    for (const child of argsNode.children ?? []) {
        const target = child.type === 'argument' ? child.children?.[0] : child;
        if (target?.type === 'class_constant_access_expression') {
            return target.children?.find((c) => c.type === 'name')?.text ?? null;
        }
    }
    return null;
}
/** Extract controller class name from arguments: [Controller::class, 'method'] or 'Controller@method' */
function extractControllerTarget(argsNode) {
    if (!argsNode)
        return { controller: null, method: null };
    const args = [];
    for (const child of argsNode.children ?? []) {
        if (child.type === 'argument')
            args.push(child.children?.[0]);
        else if (child.type !== '(' && child.type !== ')' && child.type !== ',')
            args.push(child);
    }
    // Second arg is the handler
    const handlerNode = args[1];
    if (!handlerNode)
        return { controller: null, method: null };
    // Array syntax: [UserController::class, 'index']
    if (handlerNode.type === 'array_creation_expression') {
        let controller = null;
        let method = null;
        const elements = [];
        for (const el of handlerNode.children ?? []) {
            if (el.type === 'array_element_initializer')
                elements.push(el);
        }
        if (elements[0]) {
            const classAccess = findDescendant(elements[0], 'class_constant_access_expression');
            if (classAccess) {
                controller = classAccess.children?.find((c) => c.type === 'name')?.text ?? null;
            }
        }
        if (elements[1]) {
            const str = findDescendant(elements[1], 'string');
            method = str ? extractStringContent(str) : null;
        }
        return { controller, method };
    }
    // String syntax: 'UserController@index'
    if (handlerNode.type === 'string' || handlerNode.type === 'encapsed_string') {
        const text = extractStringContent(handlerNode);
        if (text?.includes('@')) {
            const [controller, method] = text.split('@');
            return { controller, method };
        }
    }
    // Class reference: UserController::class (invokable controller)
    if (handlerNode.type === 'class_constant_access_expression') {
        const controller = handlerNode.children?.find((c) => c.type === 'name')?.text ?? null;
        return { controller, method: '__invoke' };
    }
    return { controller: null, method: null };
}
/**
 * Unwrap a chained call like Route::middleware('auth')->prefix('api')->group(fn)
 */
function unwrapRouteChain(node) {
    if (node.type !== 'member_call_expression')
        return null;
    const terminalMethod = getCallMethodName(node);
    if (!terminalMethod)
        return null;
    const terminalArgs = getArguments(node);
    const attributes = [];
    let current = node.children?.[0];
    while (current) {
        if (current.type === 'member_call_expression') {
            const method = getCallMethodName(current);
            const args = getArguments(current);
            if (method)
                attributes.unshift({ method, argsNode: args });
            current = current.children?.[0];
        }
        else if (current.type === 'scoped_call_expression') {
            const obj = current.childForFieldName?.('object') ?? current.children?.[0];
            if (obj?.text !== 'Route')
                return null;
            const method = getCallMethodName(current);
            const args = getArguments(current);
            if (method)
                attributes.unshift({ method, argsNode: args });
            return { isRouteFacade: true, terminalMethod, attributes, terminalArgs, node };
        }
        else {
            break;
        }
    }
    return null;
}
/** Parse Route::group(['middleware' => ..., 'prefix' => ...], fn) array syntax */
function parseArrayGroupArgs(argsNode) {
    const ctx = { middleware: [], prefix: null, controller: null };
    if (!argsNode)
        return ctx;
    for (const child of argsNode.children ?? []) {
        const target = child.type === 'argument' ? child.children?.[0] : child;
        if (target?.type === 'array_creation_expression') {
            for (const el of target.children ?? []) {
                if (el.type !== 'array_element_initializer')
                    continue;
                const children = el.children ?? [];
                const arrowIdx = children.findIndex((c) => c.type === '=>');
                if (arrowIdx === -1)
                    continue;
                const key = extractStringContent(children[arrowIdx - 1]);
                const val = children[arrowIdx + 1];
                if (key === 'middleware') {
                    if (val?.type === 'string') {
                        const s = extractStringContent(val);
                        if (s)
                            ctx.middleware.push(s);
                    }
                    else if (val?.type === 'array_creation_expression') {
                        for (const item of val.children ?? []) {
                            if (item.type === 'array_element_initializer') {
                                const str = item.children?.find((c) => c.type === 'string');
                                const s = str ? extractStringContent(str) : null;
                                if (s)
                                    ctx.middleware.push(s);
                            }
                        }
                    }
                }
                else if (key === 'prefix') {
                    ctx.prefix = extractStringContent(val) ?? null;
                }
                else if (key === 'controller') {
                    if (val?.type === 'class_constant_access_expression') {
                        ctx.controller = val.children?.find((c) => c.type === 'name')?.text ?? null;
                    }
                }
            }
        }
    }
    return ctx;
}
function extractLaravelRoutes(tree, filePath) {
    const routes = [];
    function resolveStack(stack) {
        const middleware = [];
        let prefix = null;
        let controller = null;
        for (const ctx of stack) {
            middleware.push(...ctx.middleware);
            if (ctx.prefix)
                prefix = prefix ? `${prefix}/${ctx.prefix}`.replace(/\/+/g, '/') : ctx.prefix;
            if (ctx.controller)
                controller = ctx.controller;
        }
        return { middleware, prefix, controller };
    }
    function emitRoute(httpMethod, argsNode, lineNumber, groupStack, chainAttrs) {
        const effective = resolveStack(groupStack);
        for (const attr of chainAttrs) {
            if (attr.method === 'middleware')
                effective.middleware.push(...extractMiddlewareArg(attr.argsNode));
            if (attr.method === 'prefix') {
                const p = extractFirstStringArg(attr.argsNode);
                if (p)
                    effective.prefix = effective.prefix ? `${effective.prefix}/${p}` : p;
            }
            if (attr.method === 'controller') {
                const cls = extractClassArg(attr.argsNode);
                if (cls)
                    effective.controller = cls;
            }
        }
        const routePath = extractFirstStringArg(argsNode);
        if (ROUTE_RESOURCE_METHODS.has(httpMethod)) {
            const target = extractControllerTarget(argsNode);
            const actions = httpMethod === 'apiResource' ? API_RESOURCE_ACTIONS : RESOURCE_ACTIONS;
            for (const action of actions) {
                routes.push({
                    filePath, httpMethod, routePath,
                    controllerName: target.controller ?? effective.controller,
                    methodName: action,
                    middleware: [...effective.middleware],
                    prefix: effective.prefix,
                    lineNumber,
                });
            }
        }
        else {
            const target = extractControllerTarget(argsNode);
            routes.push({
                filePath, httpMethod, routePath,
                controllerName: target.controller ?? effective.controller,
                methodName: target.method,
                middleware: [...effective.middleware],
                prefix: effective.prefix,
                lineNumber,
            });
        }
    }
    function walk(node, groupStack) {
        // Case 1: Simple Route::get(...), Route::post(...), etc.
        if (isRouteStaticCall(node)) {
            const method = getCallMethodName(node);
            if (method && (ROUTE_HTTP_METHODS.has(method) || ROUTE_RESOURCE_METHODS.has(method))) {
                emitRoute(method, getArguments(node), node.startPosition.row, groupStack, []);
                return;
            }
            if (method === 'group') {
                const argsNode = getArguments(node);
                const groupCtx = parseArrayGroupArgs(argsNode);
                const body = findClosureBody(argsNode);
                if (body) {
                    groupStack.push(groupCtx);
                    walkChildren(body, groupStack);
                    groupStack.pop();
                }
                return;
            }
        }
        // Case 2: Fluent chain — Route::middleware(...)->group(...) or Route::middleware(...)->get(...)
        const chain = unwrapRouteChain(node);
        if (chain) {
            if (chain.terminalMethod === 'group') {
                const groupCtx = { middleware: [], prefix: null, controller: null };
                for (const attr of chain.attributes) {
                    if (attr.method === 'middleware')
                        groupCtx.middleware.push(...extractMiddlewareArg(attr.argsNode));
                    if (attr.method === 'prefix')
                        groupCtx.prefix = extractFirstStringArg(attr.argsNode);
                    if (attr.method === 'controller')
                        groupCtx.controller = extractClassArg(attr.argsNode);
                }
                const body = findClosureBody(chain.terminalArgs);
                if (body) {
                    groupStack.push(groupCtx);
                    walkChildren(body, groupStack);
                    groupStack.pop();
                }
                return;
            }
            if (ROUTE_HTTP_METHODS.has(chain.terminalMethod) || ROUTE_RESOURCE_METHODS.has(chain.terminalMethod)) {
                emitRoute(chain.terminalMethod, chain.terminalArgs, node.startPosition.row, groupStack, chain.attributes);
                return;
            }
        }
        // Default: recurse into children
        walkChildren(node, groupStack);
    }
    function walkChildren(node, groupStack) {
        for (const child of node.children ?? []) {
            walk(child, groupStack);
        }
    }
    walk(tree.rootNode, []);
    return routes;
}
const processFileGroup = (files, language, queryString, result, onFileProcessed) => {
    let query;
    try {
        const lang = parser.getLanguage();
        query = new Parser.Query(lang, queryString);
    }
    catch (err) {
        const message = `Query compilation failed for ${language}: ${err instanceof Error ? err.message : String(err)}`;
        if (parentPort) {
            parentPort.postMessage({ type: 'warning', message });
        }
        else {
            console.warn(message);
        }
        return;
    }
    for (const file of files) {
        // Skip files larger than the max tree-sitter buffer (32 MB)
        if (file.content.length > TREE_SITTER_MAX_BUFFER)
            continue;
        let tree;
        try {
            tree = parser.parse(file.content, undefined, { bufferSize: getTreeSitterBufferSize(file.content.length) });
        }
        catch (err) {
            console.warn(`Failed to parse file ${file.path}: ${err instanceof Error ? err.message : String(err)}`);
            continue;
        }
        result.fileCount++;
        onFileProcessed?.();
        let matches;
        try {
            matches = query.matches(tree.rootNode);
        }
        catch (err) {
            console.warn(`Query execution failed for ${file.path}: ${err instanceof Error ? err.message : String(err)}`);
            continue;
        }
        // Pre-pass: extract heritage from query matches to build parentMap for buildTypeEnv.
        // Heritage edges (EXTENDS/IMPLEMENTS) are created by heritage-processor which runs
        // in PARALLEL with call-processor, so the graph edges don't exist when buildTypeEnv
        // runs. This pre-pass makes parent class information available for type resolution.
        const fileParentMap = new Map();
        for (const match of matches) {
            const captureMap = {};
            for (const c of match.captures) {
                captureMap[c.name] = c.node;
            }
            if (captureMap['heritage.class'] && captureMap['heritage.extends']) {
                const className = captureMap['heritage.class'].text;
                const parentName = captureMap['heritage.extends'].text;
                // Skip Go named fields (only anonymous fields are struct embedding)
                const extendsNode = captureMap['heritage.extends'];
                const fieldDecl = extendsNode.parent;
                if (fieldDecl?.type === 'field_declaration' && fieldDecl.childForFieldName('name'))
                    continue;
                let parents = fileParentMap.get(className);
                if (!parents) {
                    parents = [];
                    fileParentMap.set(className, parents);
                }
                if (!parents.includes(parentName))
                    parents.push(parentName);
            }
        }
        // Build per-file type environment + constructor bindings in a single AST walk.
        // Constructor bindings are verified against the SymbolTable in processCallsFromExtracted.
        const parentMap = fileParentMap;
        const typeEnv = buildTypeEnv(tree, language, { parentMap });
        const callRouter = callRouters[language];
        if (typeEnv.constructorBindings.length > 0) {
            result.constructorBindings.push({ filePath: file.path, bindings: [...typeEnv.constructorBindings] });
        }
        // Extract file-scope bindings for ExportedTypeMap (closes worker/sequential quality gap).
        // Sequential path uses collectExportedBindings(typeEnv) directly; worker path serializes
        // these bindings so the main thread can merge them into ExportedTypeMap.
        const fileScope = typeEnv.env.get('');
        if (fileScope && fileScope.size > 0) {
            const bindings = [];
            for (const [name, type] of fileScope)
                bindings.push([name, type]);
            result.typeEnvBindings.push({ filePath: file.path, bindings });
        }
        // Per-file map: decorator end-line → decorator info, for associating with definitions
        const fileDecorators = new Map();
        for (const match of matches) {
            const captureMap = {};
            for (const c of match.captures) {
                captureMap[c.name] = c.node;
            }
            // Extract import paths before skipping
            if (captureMap['import'] && captureMap['import.source']) {
                const rawImportPath = preprocessImportPath(captureMap['import.source'].text, captureMap['import'], language);
                if (!rawImportPath)
                    continue;
                const extractor = namedBindingExtractors[language];
                const namedBindings = extractor ? extractor(captureMap['import']) : undefined;
                result.imports.push({
                    filePath: file.path,
                    rawImportPath,
                    language: language,
                    ...(namedBindings ? { namedBindings } : {}),
                });
                continue;
            }
            // Extract assignment sites (field write access)
            if (captureMap['assignment'] && captureMap['assignment.receiver'] && captureMap['assignment.property']) {
                const receiverText = captureMap['assignment.receiver'].text;
                const propertyName = captureMap['assignment.property'].text;
                if (receiverText && propertyName) {
                    const srcId = findEnclosingFunctionId(captureMap['assignment'], file.path)
                        || generateId('File', file.path);
                    let receiverTypeName;
                    if (typeEnv) {
                        receiverTypeName = typeEnv.lookup(receiverText, captureMap['assignment']) ?? undefined;
                    }
                    result.assignments.push({
                        filePath: file.path,
                        sourceId: srcId,
                        receiverText,
                        propertyName,
                        ...(receiverTypeName ? { receiverTypeName } : {}),
                    });
                }
                if (!captureMap['call'])
                    continue;
            }
            // Store decorator metadata for later association with definitions
            if (captureMap['decorator'] && captureMap['decorator.name']) {
                const decoratorName = captureMap['decorator.name'].text;
                const decoratorArg = captureMap['decorator.arg']?.text;
                const decoratorNode = captureMap['decorator'];
                // Store by the decorator's end line — the definition follows immediately after
                fileDecorators.set(decoratorNode.endPosition.row, { name: decoratorName, arg: decoratorArg });
                if (ROUTE_DECORATOR_NAMES.has(decoratorName)) {
                    const routePath = decoratorArg || '';
                    const method = decoratorName.replace('Mapping', '').toUpperCase();
                    const httpMethod = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH'].includes(method) ? method : 'GET';
                    result.decoratorRoutes.push({
                        filePath: file.path,
                        routePath,
                        httpMethod,
                        decoratorName,
                        lineNumber: decoratorNode.startPosition.row,
                    });
                }
                // MCP/RPC tool detection: @mcp.tool(), @app.tool(), @server.tool()
                if (decoratorName === 'tool') {
                    // Re-store with isTool flag for the definition handler
                    fileDecorators.set(decoratorNode.endPosition.row, { name: decoratorName, arg: decoratorArg, isTool: true });
                }
                continue;
            }
            // Extract HTTP consumer URLs: fetch(), axios.get(), $.get(), requests.get(), etc.
            if (captureMap['route.fetch']) {
                const urlNode = captureMap['route.url'] ?? captureMap['route.template_url'];
                if (urlNode) {
                    result.fetchCalls.push({
                        filePath: file.path,
                        fetchURL: urlNode.text,
                        lineNumber: captureMap['route.fetch'].startPosition.row,
                    });
                }
                continue;
            }
            // HTTP client calls: axios.get('/path'), $.post('/path'), requests.get('/path')
            // Skip methods also in EXPRESS_ROUTE_METHODS to avoid double-registering Express
            // routes as both route definitions AND consumers (both queries match same AST node)
            if (captureMap['http_client'] && captureMap['http_client.url']) {
                const method = captureMap['http_client.method']?.text;
                const url = captureMap['http_client.url'].text;
                if (method && HTTP_CLIENT_ONLY_METHODS.has(method) && url.startsWith('/')) {
                    result.fetchCalls.push({
                        filePath: file.path,
                        fetchURL: url,
                        lineNumber: captureMap['http_client'].startPosition.row,
                    });
                }
                continue;
            }
            // Express/Hono route registration: app.get('/path', handler)
            if (captureMap['express_route'] && captureMap['express_route.method'] && captureMap['express_route.path']) {
                const method = captureMap['express_route.method'].text;
                const routePath = captureMap['express_route.path'].text;
                if (EXPRESS_ROUTE_METHODS.has(method) && routePath.startsWith('/')) {
                    const httpMethod = method === 'all' || method === 'use' || method === 'route' ? 'GET' : method.toUpperCase();
                    result.decoratorRoutes.push({
                        filePath: file.path,
                        routePath,
                        httpMethod,
                        decoratorName: `express.${method}`,
                        lineNumber: captureMap['express_route'].startPosition.row,
                    });
                }
                continue;
            }
            // Extract call sites
            if (captureMap['call']) {
                const callNameNode = captureMap['call.name'];
                if (callNameNode) {
                    const calledName = callNameNode.text;
                    // Dispatch: route language-specific calls (heritage, properties, imports)
                    const routed = callRouter(calledName, captureMap['call']);
                    if (routed) {
                        if (routed.kind === 'skip')
                            continue;
                        if (routed.kind === 'import') {
                            result.imports.push({
                                filePath: file.path,
                                rawImportPath: routed.importPath,
                                language,
                            });
                            continue;
                        }
                        if (routed.kind === 'heritage') {
                            for (const item of routed.items) {
                                result.heritage.push({
                                    filePath: file.path,
                                    className: item.enclosingClass,
                                    parentName: item.mixinName,
                                    kind: item.heritageKind,
                                });
                            }
                            continue;
                        }
                        if (routed.kind === 'properties') {
                            const propEnclosingClassId = findEnclosingClassId(captureMap['call'], file.path);
                            for (const item of routed.items) {
                                const nodeId = generateId('Property', `${file.path}:${item.propName}`);
                                result.nodes.push({
                                    id: nodeId,
                                    label: 'Property',
                                    properties: {
                                        name: item.propName,
                                        filePath: file.path,
                                        startLine: item.startLine,
                                        endLine: item.endLine,
                                        language,
                                        isExported: true,
                                        description: item.accessorType,
                                    },
                                });
                                result.symbols.push({
                                    filePath: file.path,
                                    name: item.propName,
                                    nodeId,
                                    type: 'Property',
                                    ...(propEnclosingClassId ? { ownerId: propEnclosingClassId } : {}),
                                    ...(item.declaredType ? { declaredType: item.declaredType } : {}),
                                });
                                const fileId = generateId('File', file.path);
                                const relId = generateId('DEFINES', `${fileId}->${nodeId}`);
                                result.relationships.push({
                                    id: relId,
                                    sourceId: fileId,
                                    targetId: nodeId,
                                    type: 'DEFINES',
                                    confidence: 1.0,
                                    reason: '',
                                });
                                if (propEnclosingClassId) {
                                    result.relationships.push({
                                        id: generateId('HAS_PROPERTY', `${propEnclosingClassId}->${nodeId}`),
                                        sourceId: propEnclosingClassId,
                                        targetId: nodeId,
                                        type: 'HAS_PROPERTY',
                                        confidence: 1.0,
                                        reason: '',
                                    });
                                }
                            }
                            continue;
                        }
                        // kind === 'call' — fall through to normal call processing below
                    }
                    if (!isBuiltInOrNoise(calledName)) {
                        const callNode = captureMap['call'];
                        const sourceId = findEnclosingFunctionId(callNode, file.path)
                            || generateId('File', file.path);
                        const callForm = inferCallForm(callNode, callNameNode);
                        let receiverName = callForm === 'member' ? extractReceiverName(callNameNode) : undefined;
                        let receiverTypeName = receiverName ? typeEnv.lookup(receiverName, callNode) : undefined;
                        let receiverMixedChain;
                        // When the receiver is a complex expression (call chain, field chain, or mixed),
                        // extractReceiverName returns undefined. Walk the receiver node to build a unified
                        // mixed chain for deferred resolution in processCallsFromExtracted.
                        if (callForm === 'member' && receiverName === undefined && !receiverTypeName) {
                            const receiverNode = extractReceiverNode(callNameNode);
                            if (receiverNode) {
                                const extracted = extractMixedChain(receiverNode);
                                if (extracted && extracted.chain.length > 0) {
                                    receiverMixedChain = extracted.chain;
                                    receiverName = extracted.baseReceiverName;
                                    // Try the type environment immediately for the base receiver
                                    // (covers explicitly-typed locals and annotated parameters).
                                    if (receiverName) {
                                        receiverTypeName = typeEnv.lookup(receiverName, callNode);
                                    }
                                }
                            }
                        }
                        result.calls.push({
                            filePath: file.path,
                            calledName,
                            sourceId,
                            argCount: countCallArguments(callNode),
                            ...(callForm !== undefined ? { callForm } : {}),
                            ...(receiverName !== undefined ? { receiverName } : {}),
                            ...(receiverTypeName !== undefined ? { receiverTypeName } : {}),
                            ...(receiverMixedChain !== undefined ? { receiverMixedChain } : {}),
                        });
                    }
                }
                continue;
            }
            // Extract heritage (extends/implements)
            if (captureMap['heritage.class']) {
                if (captureMap['heritage.extends']) {
                    // Go struct embedding: the query matches ALL field_declarations with
                    // type_identifier, but only anonymous fields (no name) are embedded.
                    // Named fields like `Breed string` also match — skip them.
                    const extendsNode = captureMap['heritage.extends'];
                    const fieldDecl = extendsNode.parent;
                    const isNamedField = fieldDecl?.type === 'field_declaration'
                        && fieldDecl.childForFieldName('name');
                    if (!isNamedField) {
                        result.heritage.push({
                            filePath: file.path,
                            className: captureMap['heritage.class'].text,
                            parentName: captureMap['heritage.extends'].text,
                            kind: 'extends',
                        });
                    }
                }
                if (captureMap['heritage.implements']) {
                    result.heritage.push({
                        filePath: file.path,
                        className: captureMap['heritage.class'].text,
                        parentName: captureMap['heritage.implements'].text,
                        kind: 'implements',
                    });
                }
                if (captureMap['heritage.trait']) {
                    result.heritage.push({
                        filePath: file.path,
                        className: captureMap['heritage.class'].text,
                        parentName: captureMap['heritage.trait'].text,
                        kind: 'trait-impl',
                    });
                }
                if (captureMap['heritage.extends'] || captureMap['heritage.implements'] || captureMap['heritage.trait']) {
                    continue;
                }
            }
            const nodeLabel = getLabelFromCaptures(captureMap, language);
            if (!nodeLabel)
                continue;
            const nameNode = captureMap['name'];
            // Synthesize name for constructors without explicit @name capture (e.g. Swift init)
            if (!nameNode && nodeLabel !== 'Constructor')
                continue;
            const nodeName = nameNode ? nameNode.text : 'init';
            const definitionNode = getDefinitionNodeFromCaptures(captureMap);
            const startLine = definitionNode ? definitionNode.startPosition.row : (nameNode ? nameNode.startPosition.row : 0);
            const nodeId = generateId(nodeLabel, `${file.path}:${nodeName}`);
            let description;
            if (language === SupportedLanguages.PHP) {
                if (nodeLabel === 'Property' && captureMap['definition.property']) {
                    description = extractPhpPropertyDescription(nodeName, captureMap['definition.property']) ?? undefined;
                }
                else if (nodeLabel === 'Method' && captureMap['definition.method']) {
                    description = extractEloquentRelationDescription(captureMap['definition.method']) ?? undefined;
                }
            }
            let frameworkHint = definitionNode
                ? detectFrameworkFromAST(language, (definitionNode.text || '').slice(0, 300))
                : null;
            // Decorators appear on lines immediately before their definition; allow up to
            // MAX_DECORATOR_SCAN_LINES gap for blank lines / multi-line decorator stacks.
            const MAX_DECORATOR_SCAN_LINES = 5;
            if (definitionNode) {
                const defStartLine = definitionNode.startPosition.row;
                for (let checkLine = defStartLine - 1; checkLine >= Math.max(0, defStartLine - MAX_DECORATOR_SCAN_LINES); checkLine--) {
                    const dec = fileDecorators.get(checkLine);
                    if (dec) {
                        // Use first (closest) decorator found for framework hint
                        if (!frameworkHint) {
                            frameworkHint = {
                                framework: 'decorator',
                                entryPointMultiplier: 1.2,
                                reason: `@${dec.name}${dec.arg ? `("${dec.arg}")` : ''}`,
                            };
                        }
                        // Emit tool definition if this is a @tool decorator
                        if (dec.isTool) {
                            result.toolDefs.push({
                                filePath: file.path,
                                toolName: nodeName,
                                description: dec.arg || '',
                                lineNumber: definitionNode.startPosition.row,
                            });
                        }
                        fileDecorators.delete(checkLine);
                    }
                }
            }
            let parameterCount;
            let requiredParameterCount;
            let parameterTypes;
            let returnType;
            let declaredType;
            if (nodeLabel === 'Function' || nodeLabel === 'Method' || nodeLabel === 'Constructor') {
                const sig = extractMethodSignature(definitionNode);
                parameterCount = sig.parameterCount;
                requiredParameterCount = sig.requiredParameterCount;
                parameterTypes = sig.parameterTypes;
                returnType = sig.returnType;
                // Language-specific return type fallback (e.g. Ruby YARD @return [Type])
                // Also upgrades uninformative AST types like PHP `array` with PHPDoc `@return User[]`
                if ((!returnType || returnType === 'array' || returnType === 'iterable') && definitionNode) {
                    const tc = typeConfigs[language];
                    if (tc?.extractReturnType) {
                        const docReturn = tc.extractReturnType(definitionNode);
                        if (docReturn)
                            returnType = docReturn;
                    }
                }
            }
            else if (nodeLabel === 'Property' && definitionNode) {
                // Extract the declared type for property/field nodes.
                // Walk the definition node for type annotation children.
                declaredType = extractPropertyDeclaredType(definitionNode);
            }
            result.nodes.push({
                id: nodeId,
                label: nodeLabel,
                properties: {
                    name: nodeName,
                    filePath: file.path,
                    startLine: definitionNode ? definitionNode.startPosition.row : startLine,
                    endLine: definitionNode ? definitionNode.endPosition.row : startLine,
                    language: language,
                    isExported: isNodeExported(nameNode || definitionNode, nodeName, language),
                    ...(frameworkHint ? {
                        astFrameworkMultiplier: frameworkHint.entryPointMultiplier,
                        astFrameworkReason: frameworkHint.reason,
                    } : {}),
                    ...(description !== undefined ? { description } : {}),
                    ...(parameterCount !== undefined ? { parameterCount } : {}),
                    ...(requiredParameterCount !== undefined ? { requiredParameterCount } : {}),
                    ...(parameterTypes !== undefined ? { parameterTypes } : {}),
                    ...(returnType !== undefined ? { returnType } : {}),
                },
            });
            // Compute enclosing class for Method/Constructor/Property/Function — used for both ownerId and HAS_METHOD
            // Function is included because Kotlin/Rust/Python capture class methods as Function nodes
            const needsOwner = nodeLabel === 'Method' || nodeLabel === 'Constructor' || nodeLabel === 'Property' || nodeLabel === 'Function';
            const enclosingClassId = needsOwner ? findEnclosingClassId(nameNode || definitionNode, file.path) : null;
            result.symbols.push({
                filePath: file.path,
                name: nodeName,
                nodeId,
                type: nodeLabel,
                ...(parameterCount !== undefined ? { parameterCount } : {}),
                ...(requiredParameterCount !== undefined ? { requiredParameterCount } : {}),
                ...(parameterTypes !== undefined ? { parameterTypes } : {}),
                ...(returnType !== undefined ? { returnType } : {}),
                ...(declaredType !== undefined ? { declaredType } : {}),
                ...(enclosingClassId ? { ownerId: enclosingClassId } : {}),
            });
            const fileId = generateId('File', file.path);
            const relId = generateId('DEFINES', `${fileId}->${nodeId}`);
            result.relationships.push({
                id: relId,
                sourceId: fileId,
                targetId: nodeId,
                type: 'DEFINES',
                confidence: 1.0,
                reason: '',
            });
            // ── HAS_METHOD / HAS_PROPERTY: link member to enclosing class ──
            if (enclosingClassId) {
                const memberEdgeType = nodeLabel === 'Property' ? 'HAS_PROPERTY' : 'HAS_METHOD';
                result.relationships.push({
                    id: generateId(memberEdgeType, `${enclosingClassId}->${nodeId}`),
                    sourceId: enclosingClassId,
                    targetId: nodeId,
                    type: memberEdgeType,
                    confidence: 1.0,
                    reason: '',
                });
            }
        }
        // Extract Laravel routes from route files via procedural AST walk
        if (language === SupportedLanguages.PHP && (file.path.includes('/routes/') || file.path.startsWith('routes/')) && file.path.endsWith('.php')) {
            const extractedRoutes = extractLaravelRoutes(tree, file.path);
            result.routes.push(...extractedRoutes);
        }
    }
};
// ============================================================================
// Worker message handler — supports sub-batch streaming
// ============================================================================
/** Accumulated result across sub-batches */
let accumulated = {
    nodes: [], relationships: [], symbols: [],
    imports: [], calls: [], assignments: [], heritage: [], routes: [], fetchCalls: [], decoratorRoutes: [], toolDefs: [], constructorBindings: [], typeEnvBindings: [], skippedLanguages: {}, fileCount: 0,
};
let cumulativeProcessed = 0;
const mergeResult = (target, src) => {
    target.nodes.push(...src.nodes);
    target.relationships.push(...src.relationships);
    target.symbols.push(...src.symbols);
    target.imports.push(...src.imports);
    target.calls.push(...src.calls);
    target.assignments.push(...src.assignments);
    target.heritage.push(...src.heritage);
    target.routes.push(...src.routes);
    target.fetchCalls.push(...src.fetchCalls);
    target.decoratorRoutes.push(...src.decoratorRoutes);
    target.toolDefs.push(...src.toolDefs);
    target.constructorBindings.push(...src.constructorBindings);
    target.typeEnvBindings.push(...src.typeEnvBindings);
    for (const [lang, count] of Object.entries(src.skippedLanguages)) {
        target.skippedLanguages[lang] = (target.skippedLanguages[lang] || 0) + count;
    }
    target.fileCount += src.fileCount;
};
parentPort.on('message', (msg) => {
    try {
        // Sub-batch mode: { type: 'sub-batch', files: [...] }
        if (msg && msg.type === 'sub-batch') {
            const result = processBatch(msg.files, (filesProcessed) => {
                parentPort.postMessage({ type: 'progress', filesProcessed: cumulativeProcessed + filesProcessed });
            });
            cumulativeProcessed += result.fileCount;
            mergeResult(accumulated, result);
            // Signal ready for next sub-batch
            parentPort.postMessage({ type: 'sub-batch-done' });
            return;
        }
        // Flush: send accumulated results
        if (msg && msg.type === 'flush') {
            parentPort.postMessage({ type: 'result', data: accumulated });
            // Reset for potential reuse
            accumulated = { nodes: [], relationships: [], symbols: [], imports: [], calls: [], assignments: [], heritage: [], routes: [], fetchCalls: [], decoratorRoutes: [], toolDefs: [], constructorBindings: [], typeEnvBindings: [], skippedLanguages: {}, fileCount: 0 };
            cumulativeProcessed = 0;
            return;
        }
        // Legacy single-message mode (backward compat): array of files
        if (Array.isArray(msg)) {
            const result = processBatch(msg, (filesProcessed) => {
                parentPort.postMessage({ type: 'progress', filesProcessed });
            });
            parentPort.postMessage({ type: 'result', data: result });
            return;
        }
    }
    catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        parentPort.postMessage({ type: 'error', error: message });
    }
});
