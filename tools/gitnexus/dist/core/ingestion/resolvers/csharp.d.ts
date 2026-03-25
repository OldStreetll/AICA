/**
 * C# namespace import resolution.
 * Handles using-directive resolution via .csproj root namespace stripping.
 */
import type { SuffixIndex } from './utils.js';
/** C# project config parsed from .csproj files */
export interface CSharpProjectConfig {
    /** Root namespace from <RootNamespace> or assembly name (default: project directory name) */
    rootNamespace: string;
    /** Directory containing the .csproj file */
    projectDir: string;
}
/**
 * Resolve a C# using-directive import path to matching .cs files.
 * Tries single-file match first, then directory match for namespace imports.
 */
export declare function resolveCSharpImport(importPath: string, csharpConfigs: CSharpProjectConfig[], normalizedFileList: string[], allFileList: string[], index?: SuffixIndex): string[];
/**
 * Compute the directory suffix for a C# namespace import (for PackageMap).
 * Returns a suffix like "/ProjectDir/Models/" or null if no config matches.
 */
export declare function resolveCSharpNamespaceDir(importPath: string, csharpConfigs: CSharpProjectConfig[]): string | null;
