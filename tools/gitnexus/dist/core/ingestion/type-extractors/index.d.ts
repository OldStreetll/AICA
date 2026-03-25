/**
 * Per-language type extraction configurations.
 * Assembled here into a dispatch map keyed by SupportedLanguages.
 */
import type { LanguageTypeConfig } from './types.js';
export declare const typeConfigs: {
    javascript: LanguageTypeConfig;
    typescript: LanguageTypeConfig;
    java: LanguageTypeConfig;
    kotlin: LanguageTypeConfig;
    csharp: LanguageTypeConfig;
    go: LanguageTypeConfig;
    rust: LanguageTypeConfig;
    python: LanguageTypeConfig;
    swift: LanguageTypeConfig;
    c: LanguageTypeConfig;
    cpp: LanguageTypeConfig;
    php: LanguageTypeConfig;
    ruby: LanguageTypeConfig;
};
export type { LanguageTypeConfig, TypeBindingExtractor, ParameterExtractor, ConstructorBindingScanner, ForLoopExtractor, PendingAssignmentExtractor, PatternBindingExtractor, } from './types.js';
export { TYPED_PARAMETER_TYPES, extractSimpleTypeName, extractGenericTypeArgs, extractVarName, extractRubyConstructorAssignment } from './shared.js';
