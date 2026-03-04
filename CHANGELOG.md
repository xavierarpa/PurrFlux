# Changelog
All notable changes to this package will be documented in this file.

## [1.0.0] - 2026-03-04

### Added
- Initial release of PurrFlux
- `MonoPurrFlux` base class with automatic attribute subscription lifecycle (inherits `NetworkBehaviour`)
- `[MethodPurrFlux]` attribute for marking methods as networked event handlers
- `PurrFluxUtils` static utility class for dispatch, listen, and subscribe operations
- `PurrMessage` network message struct with `IPackedAuto` serialization
- String extension methods: `DispatchNet<T>()`, `DispatchNet()`, `StoreNet<T>()`, `StoreNet()`
- Full compatibility with UniFlux attributes (`[MethodFlux]`, `[StateFlux]`) on the same class
- Custom editor inspector showing attributed methods grouped by type (MethodFlux, StateFlux, MethodPurrFlux)
- Editor invoke buttons for testing in Play Mode
