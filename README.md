# Currency Profit Scanner

Read-only Dalamud plugin for comparing manually verified non-gil currency rewards against Universalis market data.

## Manual Candidate Seed

Until broader Lumina shop extraction is proven, add verified rows to the plugin config directory as `currency-candidates.json`:

```json
[
  {
    "currencyId": 0,
    "currencyName": "Verified Currency Name",
    "itemId": 0,
    "cost": 0,
    "quantityReceived": 1,
    "sourceShopName": "",
    "sourceVendorName": "",
    "sourceZone": "",
    "sourceNotes": "",
    "verificationSource": ""
  }
]
```

Rows with placeholder zero `itemId`, zero `cost`, zero `quantityReceived`, missing currency names, missing Lumina items, or non-marketable items are skipped.
