from pathlib import Path
import json
root = Path(__file__).resolve().parent.parent
codes = ['ja','en','de','fr','ko','zh-Hans','zh-Hant']
tables = {c:{} for c in codes}
for line in (root/'Tools/messages.tsv').read_text(encoding='utf-8').splitlines():
    parts=line.split('|')
    assert len(parts)==8, parts[0]
    for c,v in zip(codes,parts[1:]): tables[c][parts[0]]=v
for c,t in tables.items(): (root/f'Localization/{c}.json').write_text(json.dumps(t,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

