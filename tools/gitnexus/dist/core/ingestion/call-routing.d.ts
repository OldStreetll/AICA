/**
 * Shared Ruby call routing logic.
 *
 * Ruby expresses imports, heritage (mixins), and property definitions as
 * method calls rather than syntax-level constructs. This module provides a
 * routing function used by the CLI call-processor, CLI parse-worker, and
 * the web call-processor so that the classification logic lives in one place.
 *
 * NOTE: This file is intentionally duplicated in gitnexus-web/ because the
 * two packages have separate build targets (Node native vs WASM/browser).
 * Keep both copies in sync until a shared package is introduced.
 */
/** null = this call was not routed; fall through to default call handling */
export type CallRoutingResult = RubyCallRouting | null;
/**
 * Per-language call router.
 * IMPORTANT: Call-routed imports bypass preprocessImportPath(), so any router that
 * returns an importPath MUST validate it independently (length cap, control-char
 * rejection). See routeRubyCall for the reference implementation.
 */
export type CallRouter = (calledName: string, callNode: any) => CallRoutingResult;
/** Per-language call routing. noRouting = no special routing (normal call processing) */
export declare const callRouters: {
    javascript: CallRouter;
    typescript: CallRouter;
    python: CallRouter;
    java: CallRouter;
    kotlin: CallRouter;
    go: CallRouter;
    rust: CallRouter;
    csharp: CallRouter;
    php: CallRouter;
    swift: CallRouter;
    cpp: CallRouter;
    c: CallRouter;
    ruby: typeof routeRubyCall;
};
export type RubyCallRouting = {
    kind: 'import';
    importPath: string;
    isRelative: boolean;
} | {
    kind: 'heritage';
    items: RubyHeritageItem[];
} | {
    kind: 'properties';
    items: RubyPropertyItem[];
} | {
    kind: 'call';
} | {
    kind: 'skip';
};
export interface RubyHeritageItem {
    enclosingClass: string;
    mixinName: string;
    heritageKind: 'include' | 'extend' | 'prepend';
}
export type RubyAccessorType = 'attr_accessor' | 'attr_reader' | 'attr_writer';
export interface RubyPropertyItem {
    propName: string;
    accessorType: RubyAccessorType;
    startLine: number;
    endLine: number;
    /** YARD @return [Type] annotation preceding the attr_accessor call */
    declaredType?: string;
}
/**
 * Classify a Ruby call node and extract its semantic payload.
 *
 * @param calledName - The method name (e.g. 'require', 'include', 'attr_accessor')
 * @param callNode   - The tree-sitter `call` AST node
 * @returns A discriminated union describing the call's semantic role
 */
export declare function routeRubyCall(calledName: string, callNode: any): RubyCallRouting;
