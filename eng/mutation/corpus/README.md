# Mutation-oracle regression corpus

C#-invalid programs that vib-self (managed and native stage2) accepted or crashed on (docs/MUTATION.md).
Any such accept is a defect.
`run-mutation.sh ci` runs every file here unmodified, as a baseline probe. `<KEY>.cs` is a reduced campaign
repro and `hand-*.cs` a hand-written one. While a key is listed in `../known-open-ids.txt` its files are
reported but do not fail the gate. Once its fix lands, remove the key from that file, and these files then
keep it fixed. Do not edit a file to make it pass; add a new file for a new shape.

`hand-c6-*.cs` are hand-minimized shapes that c6 self accepted before its fixes (c6 replay, the self-fix test
classes), and `c6-<KEY>.cs` a campaign or CI repro that c6 accepted for a key whose older corpus files c6 rejected.

`pending/` holds shapes whose fix is assigned but not in the tree yet: programs that self still accepts, and one
that self rejects but on which the tool's in-process Analyzer throws (AD0001). The gate reads
only the files at the top level of this directory, so `pending/` is not gated (the tool's self-test checks that).
When a fix lands, move its files from `pending/` up into this directory (see `pending/README.md`); from then on
they keep the fix in place.
