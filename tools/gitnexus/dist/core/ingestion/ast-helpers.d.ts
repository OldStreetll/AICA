import type Parser from 'tree-sitter';
import { SupportedLanguages } from '../../config/supported-languages.js';
import type { NodeLabel } from '../graph/types.js';
/** Tree-sitter AST node. Re-exported for use across ingestion modules. */
export type SyntaxNode = Parser.SyntaxNode;
/**
 * Ordered list of definition capture keys for tree-sitter query matches.
 * Used to extract the definition node from a capture map.
 */
export declare const DEFINITION_CAPTURE_KEYS: readonly ["definition.function", "definition.class", "definition.interface", "definition.method", "definition.struct", "definition.enum", "definition.namespace", "definition.module", "definition.trait", "definition.impl", "definition.type", "definition.const", "definition.static", "definition.typedef", "definition.macro", "definition.union", "definition.property", "definition.record", "definition.delegate", "definition.annotation", "definition.constructor", "definition.template"];
/** Extract the definition node from a tree-sitter query capture map. */
export declare const getDefinitionNodeFromCaptures: (captureMap: Record<string, any>) => SyntaxNode | null;
/**
 * Node types that represent function/method definitions across languages.
 * Used to find the enclosing function for a call site.
 */
export declare const FUNCTION_NODE_TYPES: Set<string>;
/**
 * Node types for standard function declarations that need C/C++ declarator handling.
 * Used by extractFunctionName to determine how to extract the function name.
 */
export declare const FUNCTION_DECLARATION_TYPES: Set<string>;
/** AST node types that represent a class-like container (for HAS_METHOD edge extraction) */
export declare const CLASS_CONTAINER_TYPES: Set<string>;
export declare const CONTAINER_TYPE_TO_LABEL: Record<string, string>;
/** Check if a Kotlin function_declaration capture is inside a class_body (i.e., a method).
 *  Kotlin grammar uses function_declaration for both top-level functions and class methods.
 *  Returns true when the captured definition node has a class_body ancestor. */
export declare function isKotlinClassMethod(captureNode: {
    parent?: any;
} | null | undefined): boolean;
/**
 * C/C++: check if a Function capture is inside a class/struct body.
 * If true, the function is already captured by @definition.method and should be skipped
 * to prevent double-indexing in globalIndex.
 */
export declare function isCppDuplicateClassFunction(functionNode: {
    parent?: any;
} | null | undefined, nodeLabel: string, language: SupportedLanguages): boolean;
/**
 * Determine the graph node label from a tree-sitter capture map.
 * Handles language-specific reclassification (C/C++ duplicate skipping, Kotlin Method promotion).
 * Returns null if the capture should be skipped (import, call, C/C++ duplicate, missing name).
 */
export declare function getLabelFromCaptures(captureMap: Record<string, any>, language: SupportedLanguages): NodeLabel | null;
/** Walk up AST to find enclosing class/struct/interface/impl, return its generateId or null.
 *  For Go method_declaration nodes, extracts receiver type (e.g. `func (u *User) Save()` → User struct). */
export declare const findEnclosingClassId: (node: any, filePath: string) => string | null;
/**
 * Find a child of `childType` within a sibling node of `siblingType`.
 * Used for Kotlin AST traversal where visibility_modifier lives inside a modifiers sibling.
 */
export declare const findSiblingChild: (parent: any, siblingType: string, childType: string) => any | null;
/**
 * Extract function name and label from a function_definition or similar AST node.
 * Handles C/C++ qualified_identifier (ClassName::MethodName) and other language patterns.
 */
export declare const extractFunctionName: (node: SyntaxNode) => {
    funcName: string | null;
    label: string;
};
export interface MethodSignature {
    parameterCount: number | undefined;
    /** Number of required (non-optional, non-default) parameters.
     *  Only set when fewer than parameterCount — enables range-based arity filtering.
     *  undefined means all parameters are required (or metadata unavailable). */
    requiredParameterCount: number | undefined;
    /** Per-parameter type names extracted via extractSimpleTypeName.
     *  Only populated for languages with method overloading (Java, Kotlin, C#, C++).
     *  undefined (not []) when no types are extractable — avoids empty array allocations. */
    parameterTypes: string[] | undefined;
    returnType: string | undefined;
}
/** Argument list node types shared between extractMethodSignature and countCallArguments. */
export declare const CALL_ARGUMENT_LIST_TYPES: Set<string>;
/**
 * Extract parameter count and return type text from an AST method/function node.
 * Works across languages by looking for common AST patterns.
 */
export declare const extractMethodSignature: (node: SyntaxNode | null | undefined) => MethodSignature;
