import { KnowledgeGraph } from '../graph/types.js';
import { ASTCache } from './ast-cache.js';
import type { ExtractedImport } from './workers/parse-worker.js';
import type { ResolutionContext } from './resolution-context.js';
import type { SuffixIndex } from './resolvers/index.js';
export type { SuffixIndex, TsconfigPaths, GoModuleConfig, CSharpProjectConfig, ComposerConfig } from './resolvers/index.js';
export type ImportMap = Map<string, Set<string>>;
export type PackageMap = Map<string, Set<string>>;
export interface NamedImportBinding {
    sourcePath: string;
    exportedName: string;
}
export type NamedImportMap = Map<string, Map<string, NamedImportBinding>>;
/**
 * Check if a file path is directly inside a package directory identified by its suffix.
 * Used by the symbol resolver for Go and C# directory-level import matching.
 */
export declare function isFileInPackageDir(filePath: string, dirSuffix: string): boolean;
/** Pre-built lookup structures for import resolution. Build once, reuse across chunks. */
export interface ImportResolutionContext {
    allFilePaths: Set<string>;
    allFileList: string[];
    normalizedFileList: string[];
    index: SuffixIndex;
    resolveCache: Map<string, string | null>;
}
export declare function buildImportResolutionContext(allPaths: string[]): ImportResolutionContext;
export declare const processImports: (graph: KnowledgeGraph, files: {
    path: string;
    content: string;
}[], astCache: ASTCache, ctx: ResolutionContext, onProgress?: (current: number, total: number) => void, repoRoot?: string, allPaths?: string[]) => Promise<void>;
export declare const processImportsFromExtracted: (graph: KnowledgeGraph, files: {
    path: string;
}[], extractedImports: ExtractedImport[], ctx: ResolutionContext, onProgress?: (current: number, total: number) => void, repoRoot?: string, prebuiltCtx?: ImportResolutionContext) => Promise<void>;
