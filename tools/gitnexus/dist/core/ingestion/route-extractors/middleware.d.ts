/**
 * Middleware chain extraction from route handler file content.
 * Detects wrapper patterns like: export const POST = withA(withB(withC(handler)))
 */
/** Keywords that terminate middleware chain walking (not wrapper function names) */
export declare const MIDDLEWARE_STOP_KEYWORDS: Set<string>;
/**
 * Extract middleware wrapper chain from a route handler file.
 * Detects patterns like: export const POST = withA(withB(withC(handler)))
 * Returns an object with the wrapper function names (outermost-first) and the
 * HTTP method they were captured from, or undefined if no chain found.
 */
export declare function extractMiddlewareChain(content: string): {
    chain: string[];
    method: string;
} | undefined;
