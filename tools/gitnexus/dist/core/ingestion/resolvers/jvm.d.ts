/**
 * JVM import resolution (Java + Kotlin).
 * Handles wildcard imports, member/static imports, and Kotlin-specific patterns.
 */
import type { SuffixIndex } from './utils.js';
import type { SyntaxNode } from '../utils.js';
/** Kotlin file extensions for JVM resolver reuse */
export declare const KOTLIN_EXTENSIONS: readonly string[];
/**
 * Append .* to a Kotlin import path if the AST has a wildcard_import sibling node.
 * Pure function — returns a new string without mutating the input.
 */
export declare const appendKotlinWildcard: (importPath: string, importNode: SyntaxNode) => string;
/**
 * Resolve a JVM wildcard import (com.example.*) to all matching files.
 * Works for both Java (.java) and Kotlin (.kt, .kts).
 */
export declare function resolveJvmWildcard(importPath: string, normalizedFileList: string[], allFileList: string[], extensions: readonly string[], index?: SuffixIndex): string[];
/**
 * Try to resolve a JVM member/static import by stripping the member name.
 * Java: "com.example.Constants.VALUE" -> resolve "com.example.Constants"
 * Kotlin: "com.example.Constants.VALUE" -> resolve "com.example.Constants"
 */
export declare function resolveJvmMemberImport(importPath: string, normalizedFileList: string[], allFileList: string[], extensions: readonly string[], index?: SuffixIndex): string | null;
