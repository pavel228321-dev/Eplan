#!/usr/bin/env python3
"""Build 1260-cables.tsv from the 1260 project PDF (report "Перечень кабелей").

Usage: generate_1260_cables.py <1260_Milk_storage_site.pdf> <out.tsv>
Columns: No, Name, Type, Cores, Sources, Targets; an end list is "LOC|DEV;LOC|DEV".
The cable type is printed on the line right above each row in the report.
"""
import re
import subprocess
import sys

ROW = re.compile(r'^(\d+)  (\+\S+)  (W\S*)  (\S+)  (\d+)  (\d+)(.*)$')


def ends(text):
    out = []
    for part in text.split(';'):
        m = re.match(r'^(\+\S+)?\s*\[(.*)\]$', part.strip())
        if m and m.group(2):
            device = re.sub(r'[^A-Za-z0-9]+$', '', m.group(2))
            out.append((m.group(1) or '') + '|' + device)
    return ';'.join(out)


def main(pdf, out_path):
    text = subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True, check=True).stdout
    rows = {}
    prev = ''
    for raw in text.split('\n'):
        line = re.sub(r'  +', '  ', raw).strip()
        m = ROW.match(line)
        if m:
            rest = [x for x in m.group(7).split('  ') if x.strip()]
            if len(rest) >= 2:
                src, tgt = ends(rest[0]), ends(rest[1])
            elif len(rest) == 1:
                one = ends(rest[0])
                src, tgt = ('', one) if one.startswith('+FIELD|') else (one, '')
            else:
                src, tgt = '', ''
            # A long source list may wrap onto the type line: "<type>  +LOC [DEV];..."
            ctype, _, overflow = prev.partition('  +')
            if overflow and not src:
                src = ends('+' + overflow)
            rows.setdefault(int(m.group(1)), (m.group(3), ctype, m.group(5), src, tgt))
        if line:
            prev = line
    with open(out_path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('No\tName\tType\tCores\tSources\tTargets\n')
        for no in sorted(rows):
            name, ctype, cores, src, tgt = rows[no]
            f.write(f'{no}\t{name}\t{ctype}\t{cores}\t{src}\t{tgt}\n')
    print(f'{len(rows)} cables -> {out_path}')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
