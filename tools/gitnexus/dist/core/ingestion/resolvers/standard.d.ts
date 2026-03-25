/**
 * Standard import path resolution.
 * Handles relative imports, path alias rewriting, and generic suffix matching.
 * Used as the fallback when language-specific resolvers don't match.
 */
import type { SuffixIndex } from './utils.js';
import { SupportedLanguages } from '../../../config/supported-languages.js';
/** TypeScript path alias config parsed from tsconfig.json */
export interface TsconfigPaths {
    /** Map of alias prefix -> target prefix (e.g., "@/" -> "src/") */
    aliases: Map<string, string>;
    /** Base URL for path resolution (relative to repo root) */
    baseUrl: string;
}
/** Max entries in the resolve cache. Beyond this, entries are evicted.
 *  100K entries ≈ 15MB — covers the most common import patterns. */
export declare const RESOLVE_CACHE_CAP = 100000;
/**
 * Resolve an import path to a file path in the repository.
 *
 * Language-specific preprocessing is applied before the generic resolution:
 * - TypeScript/JavaScript: rewrites tsconfig path aliases
 * - Rust: converts crate::/super::/self:: to relative paths
 *
 * Java wildcards and Go package imports are handled separately in processImports
 * because they resolve to multiple files.
 */
export declare const resolveImportPath: (currentFile: string, importPath: string, allFiles: Set<string>, allFileList: string[], normalizedFileList: string[], resolveCache: Map<string, string | null>, language: SupportedLanguages, tsconfigPaths: TsconfigPaths | null, index?: SuffixIndex) => string | null;
