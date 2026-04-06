/**
 * Python import resolution — PEP 328 relative imports and proximity-based bare imports.
 * Import system spec: PEP 302 (original), PEP 451 (current).
 */
/**
 * Resolve a Python import to a file path.
 *
 * 1. Relative (PEP 328): `.module`, `..module` — 1 dot = current package, each extra dot goes up one level.
 * 2. Proximity bare import: static heuristic — checks the importer's own directory first.
 *    Approximates the common case where co-located files find each other without an installed package.
 *    Single-segment only — multi-segment (e.g. `os.path`) falls through to suffixResolve.
 *    Checks package (__init__.py) before module (.py), matching CPython's finder order (PEP 451 §4).
 *    Coexistence of both is physically impossible (same name = file vs directory), so the order
 *    only matters for spec compliance.
 *    Note: namespace packages (PEP 420, directory without __init__.py) are not handled.
 *
 * Returns null to let the caller fall through to suffixResolve.
 */
export declare function resolvePythonImport(currentFile: string, importPath: string, allFiles: Set<string>): string | null;
