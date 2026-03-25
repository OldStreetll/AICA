/**
 * Import Resolution Dispatch
 *
 * Per-language dispatch table for import resolution and named binding extraction.
 * Replaces the 120-line if-chain in resolveLanguageImport() and the 7-branch
 * dispatch in extractNamedBindings() with a single table lookup each.
 *
 * Follows the existing ExportChecker / CallRouter pattern:
 *   - Function aliases (not interfaces) to avoid megamorphic inline-cache issues
 *   - `satisfies Record<SupportedLanguages, ...>` for compile-time exhaustiveness
 *   - Const dispatch table — configs are accessed via ctx.configs at call time
 */
import { SupportedLanguages } from '../../config/supported-languages.js';
import type { SyntaxNode } from './utils.js';
import type { TsconfigPaths, GoModuleConfig, CSharpProjectConfig, ComposerConfig } from './resolvers/index.js';
import type { SwiftPackageConfig } from './language-config.js';
import { extractTsNamedBindings, extractPythonNamedBindings, extractKotlinNamedBindings, extractRustNamedBindings, extractPhpNamedBindings, extractCsharpNamedBindings, extractJavaNamedBindings } from './named-binding-extraction.js';
import type { ImportResolutionContext } from './import-processor.js';
/**
 * Result of resolving an import via language-specific dispatch.
 * - 'files': resolved to one or more files -> add to ImportMap
 * - 'package': resolved to a directory -> add graph edges + store dirSuffix in PackageMap
 * - null: no resolution (external dependency, etc.)
 */
export type ImportResult = {
    kind: 'files';
    files: string[];
} | {
    kind: 'package';
    files: string[];
    dirSuffix: string;
} | null;
/** Bundled language-specific configs loaded once per ingestion run. */
export interface ImportConfigs {
    tsconfigPaths: TsconfigPaths | null;
    goModule: GoModuleConfig | null;
    composerConfig: ComposerConfig | null;
    swiftPackageConfig: SwiftPackageConfig | null;
    csharpConfigs: CSharpProjectConfig[];
}
/** Full context for import resolution: file lookups + language configs. */
export interface ResolveCtx extends ImportResolutionContext {
    configs: ImportConfigs;
}
/** Per-language import resolver -- function alias matching ExportChecker/CallRouter pattern. */
export type ImportResolverFn = (rawImportPath: string, filePath: string, resolveCtx: ResolveCtx) => ImportResult;
/** A single named import binding: local name in the importing file and exported name from the source.
 *  When `isModuleAlias` is true, the binding represents a Python `import X as Y` module alias
 *  and is routed to moduleAliasMap instead of namedImportMap during import processing. */
export interface NamedBinding {
    local: string;
    exported: string;
    isModuleAlias?: boolean;
}
/**
 * Clean and preprocess a raw import source text into a resolved import path.
 * Strips quotes/angle brackets (universal) and applies language-specific
 * transformations (currently only Kotlin wildcard import detection).
 */
export declare function preprocessImportPath(sourceText: string, importNode: SyntaxNode, language: SupportedLanguages): string | null;
/**
 * Per-language import resolver dispatch table.
 * Configs are accessed via ctx.configs at call time — no factory closure needed.
 * Each resolver encapsulates the full resolution flow for its language, including
 * fallthrough to standard resolution where appropriate.
 */
export declare const importResolvers: {
    javascript: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    typescript: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    python: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    java: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    c: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    cpp: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    csharp: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    go: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    ruby: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    rust: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    php: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    kotlin: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
    swift: (raw: string, fp: string, ctx: ResolveCtx) => ImportResult;
};
/**
 * Per-language named binding extractor dispatch table.
 * Languages with whole-module import semantics (Go, Ruby, C/C++, Swift) return undefined --
 * their bindings are synthesized post-parse by synthesizeWildcardImportBindings() in pipeline.ts.
 */
export declare const namedBindingExtractors: {
    javascript: typeof extractTsNamedBindings;
    typescript: typeof extractTsNamedBindings;
    python: typeof extractPythonNamedBindings;
    java: typeof extractJavaNamedBindings;
    c: any;
    cpp: any;
    csharp: typeof extractCsharpNamedBindings;
    go: any;
    ruby: any;
    rust: typeof extractRustNamedBindings;
    php: typeof extractPhpNamedBindings;
    kotlin: typeof extractKotlinNamedBindings;
    swift: any;
};
