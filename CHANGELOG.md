#### v1.0.4 [2026-04-10]
- Add WorldLevelMatchMode enum to Inventories (Exact, CurrentOrHigher, Ignore)
- Add CurrentWorldLevel property to Inventories for convenient access
- Mark old Inventories worldLevel-only overloads as Obsolete
- Reduce private Inventory.Changed() reflection dependency in Inventories
- Fix Objects.GetName to refresh Hoverable display names on language change
- Add built-in multilingual translations for config drawer labels (English/Japanese)
- Add RegisterDefaultDrawerTranslation API for additional language support
- Adjust utilities for Valheim 0.221.12
- Change inventory utility signatures to use world level matching
- Fix inventory stack transfer and removal iteration safety
- Add functions for registering Json importers and exporters
- Change localization loading method and improve localized config metadata refresh
- Modernize project configuration
- Adjust project references for Valheim 0.217.14
- Fix Objects.GetZNetView to avoid caching failed lookups

#### v1.0.3 [2022-12-26]
- Add utility to add item to inventory
- Fix initialization for custom GUI translations
- Fix utilities for objects
- Fix null check of Unity Object with equal operator

#### v1.0.2 [2022-12-18]
- Fix default translation of custom GUI for config
- Change Inventories utility functions signature
- Add Inventories utility functions

#### v1.0.1 [2022-12-16]
- Split custom drawer for config into separate files \
  And, add a function that adding custom drawer externally
- Fix functions in the Objects utility \
  Fix a bug in GetName and GetZNetView where inherited fields could not be retrieved.
- Add functions to the Json utility \
  Add functions to convert an object to JSON string \
  Rename structure

#### v1.0.0 [2022-12-03]
- Initial release
