# Plan dimension tools

Supported release: Revit 2027, Windows x64. Model-space input coordinates and minimum lengths are millimeters. Returned element IDs are strings to preserve 64-bit IDs. Views must be non-template floor, reflected ceiling or structural plans in the active document. These tools do not save or synchronize the model.

## get_dimension_types

No arguments. Returns linear style IDs, names and family names. Use the returned ID as `dimensionStyleId`.

## dimension_walls_by_guides

```json
{
  "guideElementIds": ["12345"],
  "wallReference": "faces",
  "minimumSegmentMm": 10
}
```

Optional: `viewId`, `dimensionStyleId`, `wallIds`. Omit `wallIds` for all candidate walls in the view; an empty array selects no walls. Each guide must be a visible straight Model Line, Detail Line or Reference Plane. Its finite extent and location define the chain. The plan cut plane supplies the elevation for intersection checks. Reference Plane extent comes from BubbleEnd/FreeEnd and its intersection with the view plane.

Only straight vertical basic walls in the active document are supported. Faces must be perpendicular to the dimension direction; unsupported walls and missing references are reported. `faces` dimensions both finish faces. `core` is experimental: hidden core boundaries use internal `-9999` reference discovery, validated with temporary rolled-back dimensions. Missing core references are never replaced with finish references. Internal reference conventions are not a guaranteed public Revit API contract.

No curved, curtain, slanted or stacked walls, linked models, automatic jamb dimensions, text collision layout, or duplicate-series detection. Openings do not remove the entire wall-side reference. Repeating a call creates another series.

Stations are sorted in a canonical view direction, merged within tolerance and filtered against the last retained station. A short last segment drops its last reference. This changes the reference set, never the displayed measurement text. Every resulting segment is checked against its expected length. One Undo removes the successful batch. Failures/warnings at commit roll back the affected guide.

Returns `success`, `created`, `skipped`, per-guide `results` with dimension IDs/errors, `warnings` (up to 100 distinct messages), and `experimentalCoreReferences`. Read all fields: successful guides remain when another guide fails.

## create_dimensions

```json
{
  "dimensions": [{
    "startPoint": {"x": 0, "y": 0, "z": 0},
    "endPoint": {"x": 6000, "y": 0, "z": 0},
    "linePoint": {"x": 0, "y": -1000, "z": 0},
    "elementIds": ["111", "222"],
    "referenceMode": "exterior"
  }]
}
```

Provide at least two `references` (stable Revit reference strings), or two `elementIds`. Stable references take precedence. `startPoint` and `endPoint` define direction; `linePoint` translates the line perpendicular to that direction. Points are projected onto the view plane. With no `linePoint`, the line passes through the supplied endpoints.

For walls choose `referenceMode: exterior` (default) or `interior`. Families use `centerLeftRight`, `centerFrontBack`, `left` or `right` and must expose that reference. Grids use straight perpendicular axes; Reference Planes must be perpendicular to the dimension line. Ambiguous wall faces are rejected; use explicit stable references in that case. No generic element-reference fallback or nearest-element guessing is performed. Revit validates reference compatibility. Linked references are not supported.

Optional `viewId`, `dimensionStyleId`; `dimensionType` accepts only `Linear`/`Aligned`. Batch size 1–100. Arbitrary parameter overrides are rejected. IDs accept safe integers or decimal strings. Legacy `-1` view/style IDs still select defaults when called directly, but MCP clients should omit these fields.

Returns `success`, `created`, `failed`, and per-item `results` with `index`, `dimensionId`, `segmentLengthsMm`, or `error`. All lengths must be positive and finite. Partial success retains committed IDs, with one Undo for the successful batch. Point-only requests that previously guessed references now fail explicitly.

Source adaptation: `DimensionEngine` and `SpacingFilter` derive from the repository owner's JEMS Auto Wymiary engine (commit ce4137045a990dc65d717a42fec2cd40ea3fe704). MCP parameter parsing, structured results, explicit dimensions and deployment are maintained in this fork.
