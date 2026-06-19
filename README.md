# Currency Profit Scanner

Read-only Dalamud plugin for comparing manually verified non-gil currency rewards against Universalis market data.

## Manual Candidate Seed

Until broader Lumina shop extraction is proven, add verified rows to the plugin config directory as `currency-candidates.json`:

```json
{
  "currencies": [
    {
      "currencyId": 0,
      "currencyName": "Verified Currency Name",
      "iconId": 0,
      "maxAmount": null,
      "sourceNotes": "Manual verified"
    }
  ],
  "items": [
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
      "territoryId": null,
      "mapId": null,
      "x": null,
      "y": null,
      "z": null,
      "aetheryteId": null,
      "lifestreamCommand": null,
      "verificationSource": ""
    }
  ]
}
```

Rows with placeholder zero `itemId`, zero `cost`, zero `quantityReceived`, missing currency names, missing Lumina items, or non-marketable items are skipped.
