# Currency Profit Scanner

Read-only Dalamud plugin for choosing how to spend non-gil currencies for gil profit.

The scanner is currency-first: pick a spendable currency such as tomestones, scrips, seals, tribe currencies, or MGP, then compare marketable rewards with Universalis data for your selected world, data center, or region. Rankings are opinionated rather than player-tuned: the score blends gil per currency with 24h sale count, units sold, sale recency, active supply, and stale-data penalties so slow expensive items do not float to the top just because their listing price is high.

The built-in currency catalog intentionally mirrors Currency Spender's tracked currencies only: Grand Company seals, MGP, poetics/current tomestones, PvP currencies, hunt currencies and mark logs, bicolor gemstones and vouchers, crafter/gatherer/skybuilders' scrips, Island Sanctuary cowries, and Cosmic Exploration credits. Broader currencies can be added manually later instead of showing every Lumina spendable row.

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
  "items": []
}
```
