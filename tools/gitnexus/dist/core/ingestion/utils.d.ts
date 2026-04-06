import { SupportedLanguages } from '../../config/supported-languages.js';
/**
 * Built-in function/method names that should not be tracked as call targets.
 * Covers JS/TS, Python, Kotlin, C/C++, PHP, Swift standard library functions.
 */
export declare const BUILT_IN_NAMES: Set<string>;
/** Check if a name is a built-in function or common noise that should be filtered out */
export declare const isBuiltInOrNoise: (name: string) => boolean;
/**
 * Yield control to the event loop so spinners/progress can render.
 * Call periodically in hot loops to prevent UI freezes.
 */
export declare const yieldToEventLoop: () => Promise<void>;
/**
 * Map file extension to SupportedLanguage enum
 */
export declare const getLanguageFromFilename: (filename: string) => SupportedLanguages | null;
export declare const isVerboseIngestionEnabled: () => boolean;
export * from './ast-helpers.js';
export * from './call-analysis.js';
