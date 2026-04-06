/**
 * Rust module import resolution.
 * Handles crate::, super::, self:: prefix paths and :: separators.
 */
/**
 * Resolve Rust use-path to a file.
 * Handles crate::, super::, self:: prefixes and :: path separators.
 */
export declare function resolveRustImport(currentFile: string, importPath: string, allFiles: Set<string>): string | null;
/**
 * Try to resolve a Rust module path to a file.
 * Tries: path.rs, path/mod.rs, and with the last segment stripped
 * (last segment might be a symbol name, not a module).
 */
export declare function tryRustModulePath(modulePath: string, allFiles: Set<string>): string | null;
