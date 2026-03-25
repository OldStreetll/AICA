/**
 * Response shape extraction from route handler file content.
 * Detects .json() calls, extracts top-level keys, and classifies by HTTP status code.
 */
/**
 * Detect an HTTP status code associated with a .json() call.
 * Looks for three patterns:
 * 1. `.status(N).json(` — Express style (look backwards from .json match)
 * 2. `.json({...}, { status: N })` — NextResponse style (look after closing brace of first arg)
 * 3. `new Response(JSON.stringify({...}), { status: N })` — raw Response constructor
 *
 * Returns the numeric status code, or undefined if none found.
 */
export declare function detectStatusCode(content: string, jsonMatchPos: number, closingBracePos: number): number | undefined;
/**
 * Extract response shapes from handler file content.
 * Finds all .json({...}) calls, extracts top-level keys using brace-depth counting,
 * and classifies into success (responseKeys) vs error (errorKeys) by HTTP status code.
 */
export declare function extractResponseShapes(content: string): {
    responseKeys?: string[];
    errorKeys?: string[];
};
