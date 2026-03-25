/**
 * Go package import resolution.
 * Handles Go module path-based package imports.
 */
/** Go module config parsed from go.mod */
export interface GoModuleConfig {
    /** Module path (e.g., "github.com/user/repo") */
    modulePath: string;
}
/**
 * Extract the package directory suffix from a Go import path.
 * Returns the suffix string (e.g., "/internal/auth/") or null if invalid.
 */
export declare function resolveGoPackageDir(importPath: string, goModule: GoModuleConfig): string | null;
/**
 * Resolve a Go internal package import to all .go files in the package directory.
 * Returns an array of file paths.
 */
export declare function resolveGoPackage(importPath: string, goModule: GoModuleConfig, normalizedFileList: string[], allFileList: string[]): string[];
