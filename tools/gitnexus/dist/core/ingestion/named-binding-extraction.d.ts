import type { SymbolTable, SymbolDefinition } from './symbol-table.js';
import type { NamedImportMap } from './import-processor.js';
import type { NamedBinding } from './import-resolution.js';
import type { SyntaxNode } from './utils.js';
/**
 * Walk a named-binding re-export chain through NamedImportMap.
 *
 * When file A imports { User } from B, and B re-exports { User } from C,
 * the NamedImportMap for A points to B, but B has no User definition.
 * This function follows the chain: A→B→C until a definition is found.
 *
 * Returns the definitions found at the end of the chain, or null if the
 * chain breaks (missing binding, circular reference, or depth exceeded).
 * Max depth 5 to prevent infinite loops.
 *
 * @param allDefs Pre-computed `symbolTable.lookupFuzzy(name)` result — must be the
 *               complete unfiltered result. Passing a file-filtered subset will cause
 *               silent misses at depth=0 for non-aliased bindings.
 */
export declare function walkBindingChain(name: string, currentFilePath: string, symbolTable: SymbolTable, namedImportMap: NamedImportMap, allDefs: SymbolDefinition[]): SymbolDefinition[] | null;
export declare function extractTsNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
export declare function extractPythonNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
export declare function extractKotlinNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
export declare function extractRustNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
export declare function extractPhpNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
export declare function extractCsharpNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
export declare function extractJavaNamedBindings(importNode: SyntaxNode): NamedBinding[] | undefined;
