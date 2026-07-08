# Helix Explorer — Performance checkpoint

Manual benchmark notes for the corrective plan (Phase 8 profiling deliverable).

## Synthetic folder filter benchmark

Automated: `FilterPerformanceTests.Apply_10kEntries_CompletesQuickly` exercises `FileNameFilter.Apply` over 10,000 entries and asserts completion under 500ms.

## Suggested manual checks

1. Create a folder with 10k+ files (or use an existing large directory).
2. Open in Helix Details view; confirm initial listing returns without freezing the UI.
3. Press `Ctrl+F` and type a substring; confirm filter updates remain responsive.
4. Switch to Grid view and scroll; confirm row virtualization keeps scrolling smooth.

## dotnet-trace (optional)

```powershell
dotnet-trace collect --process-id <pid> --profile cpu-sampling
```

Collect while scrolling and filtering in a large folder. Review hot paths for `ApplySortAndPublish`, `EntryItemViewModel` allocation, and grid row rebuild.

## Corrective changes that target perf

- `EntryItemViewModel` reuse keyed by `FullPath` in `PaneViewModel.ApplySortAndPublish`
- `VirtualizingFileGrid` skips full row rebuild when column count and item count are unchanged
- Miller columns use `VirtualizingStackPanel` and cap retained columns at 8
