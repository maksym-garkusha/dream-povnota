# dream-povnota

How complete the project records in the DREAM system are: which fields are filled in out of those that are relevant at a project's stage, and what is still missing. Broken down by stage, region, sector and organisation.

**Live page: [maksym-garkusha.github.io/dream-povnota](https://maksym-garkusha.github.io/dream-povnota/)** (in Ukrainian). The data is refreshed every week.

[Українська версія](README.md)

This is an independent learning project built on DREAM open data. It is not affiliated with any government body, and its figures are not official statistics.

## Why

DREAM ([dream.gov.ua](https://dream.gov.ua)) is the system in which Ukrainian communities, regional administrations and regional restoration services manage reconstruction and public investment projects. Each project record is filled in by the project's initiator, and the level of detail varies a lot: some records have a cost estimate, funding sources, procurement and photos, while others have little more than a title.

The project answers a practical question for the people responsible for data quality in a community, a region or the system as a whole: **where exactly are records incomplete, and what is missing**. The program downloads every record through the public API, checks each one against the fields that are relevant at its stage, and publishes the result as a web page. An organisation can look itself up by name or EDRPOU code (the Ukrainian company registry number) and see a list of its records with what remains to be filled in for each.

This is not a ranking and not a compliance audit. The rules on which fields are mandatory at which stage are not published, so the wording here is deliberately neutral: a field is either "filled in" or "not filled in". Tables on the page are sorted by name by default.

## What the data shows

Snapshot of 17 November 2026: the API feed lists 16,172 projects. Of these, 14,610 are open; the remaining 1,562 are closed (cancelled as mistaken or duplicate, or not implemented) and are left out of everything below.

**Most records are well filled in.** Average completeness is 92.6%, and 8,949 of the 14,610 records (61%) contain everything that is relevant at their stage. The description, location, documents and cost estimate are present in 99% of records. In other words, initiators do the groundwork.

The gaps are concentrated in a few places, almost all of them at the implementation stage.

**Records of projects that are already under way are rarely updated.** At the implementation stage, 5,660 projects have either a funding source or a procurement. 1,398 of them, one in four, were updated in the past year. The other 4,262 were last changed more than a year ago. For a system meant to show the progress of reconstruction, this is the most visible gap.

**Procurement is recorded, but no funding source is given.** 4,678 projects have a procurement or contract. In 1,335 of them no funding source is given, although a contract implies that the money comes from somewhere.

**Funding is committed, but there is no procurement.** Of 2,107 projects with committed funding, 411 have no procurement or contract recorded.

**Almost one record in ten has no sector.** 1,384 open records have no sector classification. These projects drop out of any breakdown such as "how much goes to education and how much to health care".

**1.4% of records have English fields.** 208 of the 14,610 projects have an English title. This matters most for the 2,963 approved projects that have neither funding nor procurement yet, i.e. those looking for money: 54 of them have an English title. A foreign donor browsing the list sees them in Ukrainian only.

Smaller findings:

- in 57 open records the description is just the word `NULL`, which looks like a by-product of data migration;
- all 5,934 distinct EDRPOU codes in the records pass the check-digit test, so organisation codes in the system are in good order;
- 14 different ten-digit numbers appear in the EDRPOU field, i.e. the tax number of a sole proprietor rather than a company code. The program does not store these numbers;
- the feed contains no project created after November 2025, although existing records are still being updated. Newer projects are probably managed under the updated public investment model and are not exposed through this API. If so, the page covers projects registered between September 2023 and November 2025.

The full report with all breakdowns: [report.md](https://maksym-garkusha.github.io/dream-povnota/report.md), regenerated weekly along with the page.

## How it is calculated

### Stages

The stage comes from the record's `internal.status`:

| Status in the API | Stage |
|---|---|
| `pending:initiated` | Idea |
| `pending:defined` | Defined |
| `pending:appraised` | Appraised |
| `active:*` | Implementation |
| `complete*` | Completed |
| `cancelled*`, `unsuccessful*` | Closed: cancelled, mistaken, duplicate, not implemented |
| anything else | Unknown status |

Closed records and records with an unknown status are excluded from the checks and breakdowns; they are only counted.

### Checks

Each check applies only where the field is relevant. The list lives in a single file, [CheckList.cs](src/DreamPovnota/Checks/CheckList.cs).

| Check | Relevant | Filled in if |
|---|---|---|
| Description | all open | the Ukrainian description, with HTML removed, is not empty, not a placeholder and at least 30 characters long |
| Location (KATOTTG code) | all open | there is at least one KATOTTG code in the project or, if there is none, in its linked objects |
| Sector | all open | there is a `WB-ECO` classification (World Bank sectors) |
| Documents or photos | all open | there is at least one document in the project, its `approaches` or its linked objects |
| Duration | from Defined | `period.durationInMonth` is greater than zero |
| Purpose | from Defined | the Ukrainian `purposeDetails` is not empty and not a placeholder |
| Cost estimate | from Appraised | the sum of non-cancelled `budgetBreakdown` lines is greater than zero |
| Funding source | Implementation and Completed, if there is a non-cancelled procurement or contract | the sum of non-cancelled funding sources is greater than zero |
| Procurement or contract | Implementation and Completed, if there is `committed` funding | there is at least one non-cancelled contracting process |
| Updated in the past year | Implementation only, if there is funding or procurement | no more than 365 full days between the last change and the snapshot date |
| English title | all open | the `en` title is not empty |
| English description | all open | the `en` description, with HTML removed, is not empty and not a placeholder |

Placeholders are `NULL`, `-`, `.`, `N/A`, `НЕМАЄ` and `НЕМА` ("none" in Ukrainian), case-insensitive, as well as text that is empty once tags are removed.

**Why the three implementation checks are conditional.** The status `active:approved` means the project has been approved, not that money is available. An approved project with no funding and no procurement is in a normal state: it is looking for funding, and its empty fields are not a gap. So at the implementation stage the checks look for consistency: procurement is recorded but no funding source; funding is committed but there is no procurement; money or contracts are moving but the record has not been touched for a year. Approved projects with neither funding nor procurement are flagged separately as looking for funding, and the page has a filter for them.

**Record completeness** is the share of relevant checks that are filled in, not counting the English fields. The average completeness of a group is the mean of these shares across its records.

**Why the English fields are kept separate.** About 1.4% of records have an English title, so including the English fields would lower almost every record's score by the same amount and hide the other differences. They still matter for projects that are seeking money from foreign donors, so they are shown as a separate group, with a separate count for projects looking for funding.

**Region** is taken from the KATOTTG codes (the Ukrainian classifier of administrative units): the two digits after `UA`. If a project's codes belong to different regions it is shown under "Several regions"; if there are no codes, under "Not specified". **Organisation** is the party with the `projectAuthor` role, which maintains the record. **Sector** is the first letter of the `WB-ECO` code.

### What the method does not see

- It checks whether a field is filled in, not whether it is correct. A 30-character description that explains nothing counts as filled in, and cost estimates are not reconciled with contracts.
- It is not known which fields DREAM treats as mandatory at each stage. The checks reflect what a reader would reasonably expect from a record, not an official regulation.
- The program sees only what the public API returns. If some projects are managed in another module (see the last point under "What the data shows"), they are not included here.
- Sole proprietors who maintain records cannot be told apart: the program does not store their codes or names, so on the page they are grouped together.

## What would help in the system itself

These points are not about the people who maintain the records but about where the system could prompt them. None of them requires changing the data model.

1. **A reminder about stale records.** If a project has an active contract and the record has not been changed for six months, the person responsible gets a reminder to update its status. This would close the largest gap.
2. **A consistency prompt on save.** When someone adds a procurement to a project without a funding source, the system asks for one. Not a hard block, just a prompt at the moment the person is already in the form.
3. **Sector as a required field** at the very first step. It is currently missing in one record in ten.
4. **An automatic draft of the English title.** The project title can be machine-translated and offered to the initiator for one-click confirmation. For projects looking for funding, this is the cheapest way to become visible to donors.
5. **Not accepting placeholder words instead of text.** `NULL`, a dash or a full stop in the description field is an empty field, and the form can say so.
6. **New projects in the open API.** If projects created after November 2025 are indeed managed in another module, it would help to publish them through the same public interface, so that the open data does not stop in 2025.

## Running it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). No database is required.

```bash
git clone https://github.com/maksym-garkusha/dream-povnota.git
cd dream-povnota/src/DreamPovnota

dotnet run -- sync --cache ../../data         # download records from the API into the cache
dotnet run -- export --cache ../../data ../../site   # build site/data.json
dotnet run -- report --cache ../../data ../../docs/report.md
dotnet test                                   # from the repository root
```

The page is a plain HTML file, but it loads `data.json` with `fetch`, so it has to be served over HTTP: `python3 -m http.server` in the `site` folder is enough.

The first `sync` downloads every record; a full pass with 4 parallel requests took us about 14 minutes. Later runs fetch only the records that changed since the previous run. Other options:

- `--limit 300` takes only the first 300 projects in the feed, for a quick test;
- `--parallel 4` sets how many records are downloaded at once;
- `--as-of 2026-11-17T22:00:00Z` sets the snapshot date for `export` and `report` (now by default). "Updated in the past year" is measured from it, so the same cache and the same date always give the same figures.

If download errors exceed 2% of the feed, `sync` exits with a non-zero code so that the workflow does not publish incomplete data.

## How it works

```
src/DreamPovnota/
  Dream/     API client: paged feeds, records, retries
  Cards/     parsing JSON into project and object records, HTML cleanup, stages
  Store/     cache of parsed records in JSON Lines
  Sync/      feed, comparison with the cache, downloading what changed
  Checks/    the checks and per-record evaluation
  Geo/       regions from KATOTTG codes
  Codes/     EDRPOU check digit
  Stats/     breakdowns by stage, region, sector and organisation
  Export/    data.json for the page
  Report/    Markdown report
site/        the page: a single HTML file, no build step
docs/        the report for the snapshot the figures in this README come from
tests/       xUnit; fixtures are real API responses with personal names replaced
```

A few decisions worth explaining.

**No database.** There are about 15,000 open records; they fit in memory, and the output is two static files. The program reads the cache, computes everything in two to three seconds and writes `data.json` and the report. A database would only add setup steps.

**The cache holds parsed records, not raw responses.** A raw project record is 30 to 300 KB; a parsed one is about 1.5 KB, so the cache for the whole snapshot takes 26 MB. Raw responses also contain people's names, while parsed records do not, so the cache can safely be kept between workflow runs. The trade-off: if parsing changes and a record gains a new field, the cache has to be deleted so that every record is parsed again.

**Personal data.** The API returns the names of managers (`parties[].persons`) and lists sole proprietors as parties with the `UA-IPN` scheme, where the code is a personal tax number and the name is a person's name. The parser does not read `persons` at all, and for a sole proprietor it keeps only the role and a flag saying it is a sole proprietor. The same applies to a ten-digit number recorded under the `UA-EDR` scheme. Tests make sure that the test numbers and fictitious names from the fixtures do not appear in the cache, `data.json` or the report.

**A page without a server.** Every Monday GitHub Actions restores the cache from the previous run, downloads the changed records, builds `data.json` (4.7 MB, about 0.9 MB compressed) and the report, and publishes them with the page on GitHub Pages. Search and filters run in the browser: 15,000 rows are filtered in a few milliseconds, and only the first 50 are drawn. The filter state is kept in the page address, so a selection can be shared as a link.

**The `data.json` format.** To keep the file small, projects are stored as arrays, and organisations, regions, stages and sectors are kept in lookup lists that projects refer to by index. A project row is `[code without "DREAM-UA-", title, organisation, region, stage, sector, last change date, relevant checks, filled checks, looking for funding]`. Checks are stored as bit masks in the order of the `checks` array. Titles are cut to 160 characters.

## What is not here

- Field contents are not verified, only their presence. The program cannot tell whether a cost estimate matches the contracts or whether a description is accurate.
- There is no history: the page shows the latest snapshot. Earlier snapshots remain only in the workflow run history.
- Prozorro (the public procurement system) data is not pulled in: a procurement counts as present if it is recorded in the DREAM record.
- Organisations are not ranked by completeness or compared with each other. This is deliberate: the tool is for finding what to fill in, not for league tables.

## Data and licence

Data: the public API of the DREAM system (`public-api.dream.gov.ua`), documented at [open-contracting.github.io/dream-api-docs](https://open-contracting.github.io/dream-api-docs/). The DREAM portal states that the data is published under the Creative Commons Attribution 4.0 International licence. `tests/DreamPovnota.Tests/Fixtures` contains a few real API responses in which people's names and sole proprietor details have been replaced with fictitious ones and long arrays have been trimmed.

Code: MIT.
