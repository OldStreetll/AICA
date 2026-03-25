/**
 * PHP PSR-4 import resolution.
 * Handles use-statement resolution via composer.json autoload mappings.
 */
import type { SuffixIndex } from './utils.js';
/** PHP Composer PSR-4 autoload config */
export interface ComposerConfig {
    /** Map of namespace prefix -> directory (e.g., "App\\" -> "app/") */
    psr4: Map<string, string>;
    /** PSR-4 entries sorted by namespace length descending (longest match wins).
     *  Cached once at config load time to avoid re-sorting on every import. */
    psr4Sorted?: readonly [string, string][];
}
/**
 * Resolve a PHP use-statement import path using PSR-4 mappings.
 * e.g. "App\Http\Controllers\UserController" -> "app/Http/Controllers/UserController.php"
 *
 * For function/constant imports (use function App\Models\getUser), the last
 * segment is the symbol name, not a class name, so it may not map directly to
 * a file. When PSR-4 class-style resolution fails, we fall back to scanning
 * .php files in the namespace directory.
 *
 * NOTE: The function-import fallback returns the first matching .php file in the
 * namespace directory. When multiple files exist in the same namespace directory,
 * resolution is non-deterministic (depends on Set/index iteration order). This is
 * a known limitation — PHP function imports cannot be resolved to a specific file
 * without parsing all candidate files.
 */
export declare function resolvePhpImport(importPath: string, composerConfig: ComposerConfig | null, allFiles: Set<string>, normalizedFileList: string[], allFileList: string[], index?: SuffixIndex): string | null;
