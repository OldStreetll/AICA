/**
 * Export Detection
 *
 * Determines whether a symbol (function, class, etc.) is exported/public
 * in its language. This is a pure function — safe for use in worker threads.
 *
 * Shared between parse-worker.ts (worker pool) and parsing-processor.ts (sequential fallback).
 */
import { SyntaxNode } from './utils.js';
import { SupportedLanguages } from '../../config/supported-languages.js';
/**
 * Check if a tree-sitter node is exported/public in its language.
 * @param node - The tree-sitter AST node
 * @param name - The symbol name
 * @param language - The programming language
 * @returns true if the symbol is exported/public
 */
export declare const isNodeExported: (node: SyntaxNode, name: string, language: SupportedLanguages) => boolean;
