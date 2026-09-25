"""Build the reviewed gameplay catalog. No third-party Excel dependency.

The dictionary supplies numerical attribute rules; explicit recipes implement the
design master's behavior. The obsolete effect-parameter sheet is not imported.
"""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
NS = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
M = '{' + NS['m'] + '}'

def workbook_rows(path):
    with zipfile.ZipFile(path) as archive:
        shared = [''.join(x.itertext()) for x in ET.fromstring(archive.read('xl/sharedStrings.xml')).findall('m:si', NS)]
        rel = {r.attrib['Id']: r.attrib['Target'] for r in ET.fromstring(archive.read('xl/_rels/workbook.xml.rels'))}
        result = []
        for sheet in ET.fromstring(archive.read('xl/workbook.xml')).findall('m:sheets/m:sheet', NS):
            target = rel[sheet.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id']]
            part = target.lstrip('/') if target.startswith('/') else 'xl/' + target
            rows = []
            for row in ET.fromstring(archive.read(part)).findall('m:sheetData/m:row', NS):
                values = {}
                for cell in row.findall('m:c', NS):
                    value = cell.find('m:v', NS)
                    value = value.text if value is not None else ''
                    if cell.attrib.get('t') == 's': value = shared[int(value)]
                    elif cell.attrib.get('t') == 'inlineStr': value = ''.join(cell.find('m:is', NS).itertext())
                    values[''.join(filter(str.isalpha, cell.attrib['r']))] = value
                rows.append(values)
            result.append((sheet.attrib['name'], rows))
        return result

def generate():
    path = ROOT / 'Docs/血色柏青哥_道具药物装置_规范修订版.xlsx'
    sheets = workbook_rows(path)
    attrs = []
    for row in sheets[0][1]:
        if not row.get('A') or row.get('Q') != 'No': continue
        attrs.append(dict(Id=row['A'], Name=row['B'], Unit='m/s' if row['A']=='BALL_LAUNCH_SPEED' else row['E'],
                          DefaultValue=float(row['F']), MinValue=float(row['G']), MaxValue=float(row['H']),
                          Operators=row['I'], StackRule=row['J'], AppliesTo=row['K'], StackOrder=int(row['L'])))
    if not any(a['Id'] == 'GAMBLE_QUADRUPLE_CHANCE' for a in attrs):
        for attr in attrs:
            if attr['Id'] in ('GAMBLE_LOSS_CHANCE', 'GAMBLE_RETURN_CHANCE', 'GAMBLE_WIN_CHANCE'): attr['DefaultValue'] = .25
        attrs.append(dict(Id='GAMBLE_QUADRUPLE_CHANCE', Name='四倍概率', Unit='%', DefaultValue=.25,
                          MinValue=0, MaxValue=1, Operators='ADD,MUL,SET', StackRule='ADD→MUL→CLAMP',
                          AppliesTo='Drug,Item,Global', StackOrder=20))
    import re
    enum_source = (ROOT/'Scripts/Marbles/Rules/GameAttributes.cs').read_text(encoding='utf-8')
    enum_body = enum_source.split('public enum GameAttribute : byte')[1].split('}')[0]
    enum_ids = re.findall(r'\b[A-Z][A-Z_]+\b', enum_body)
    attr_index = {name: i for i, name in enumerate(enum_ids)}
    def effect(name, op, value):
        return dict(Attribute=attr_index[name], Operation={'ADD': 0, 'MUL': 1, 'SET': 2}[op], Value=value, Priority=0)
    device_names = ['银行','增幅塔','离心机','传送门','黑色四叶草','分流器','弹板','增值透镜','资本主义','血穿炮','减速带','复活吧我的爱人','磁磁磁']
    drug_names = ['肾上腺素','烧烧果实','延长药剂','润流剂','凝血剂','摇头丸','稀血剂','无限增值','磁堕','定向试剂（补全名）']
    prices = {'Common': 10, 'Uncommon': 18, 'Rare': 28, 'Epic': 40, 'Legendary': 60}
    devices, drugs = [], []
    for row in sheets[1][1]:
        category, name = row.get('C'), row.get('B')
        if category == '装置':
            i = device_names.index(name)
            devices.append(dict(Id=100+i, SourceId=row['A'], Name=name, Description=row.get('H',''), Kind=i+1,
                                Price=prices[row['N']], ScoreMultiplier=1, RushChanceAdd=0,
                                Radius=.04 if i==1 else .12, StartingCount=1 if i in (4,7) else 0, Cooldown=.5 if i in (2,3) else .1,
                                MaxTriggersPerBall=2 if i in (2,3,8) else 1 if i in (0,4,11) else 0,
                                Range=.2, Strength=2, Value=[.12,1.2,5,1,.75,2,5,2,3,10,.7,1,1][i]))
        elif category == '药物':
            i = drug_names.index(name)
            effects = []
            def add(attr, op, value): effects.append(effect(attr,op,value))
            if i in (0,2,4,5,6,8): add('BLOOD_COST','MUL',1.2)
            if i in (1,3,9): add('BALL_BASE_VALUE','MUL',.9)
            if i in (4,5,6): add('BALL_BASE_VALUE','MUL',1.3)
            if i == 2: add('RUSH_DURATION','MUL',1.2)
            if i == 3:
                add('BALL_FRICTION','MUL',.6); add('BALL_BOUNCE','ADD',.1)
            if i == 5: add('BALL_BOUNCE','ADD',3)
            if i == 7: add('BLOOD_COST','MUL',2)
            if i == 8: add('BALL_BASE_VALUE','MUL',1.1)
            drugs.append(dict(Id=100+i, SourceId=row['A'], Name=name, Description=row.get('H',''), Kind=i+1,
                              Price=prices[row['N']], RestitutionMultiplier=1, DurationSeconds=20 if i==9 else 10,
                              StartingCount=1 if i in (0,3) else 0, Stackable=i in (5,6,7,8), VolumeMultiplier=1.5 if i in (4,7) else 1, Effects=effects))
    output = ROOT/'Resources/GameContent.json'
    data = dict(Attributes=attrs, Devices=devices, Drugs=drugs, RushThreshold=10,
                OverkillCoinsPerScore=.01, CentimetersToUnits=.01, MaxSplitGeneration=2)
    if output.exists():
        previous = json.loads(output.read_text(encoding='utf-8-sig'))
        for key in ('Attributes', 'Devices', 'Drugs'):
            existing = previous.get(key, [])
            ids = {row['Id'] for row in existing}
            data[key] = existing + [row for row in data[key] if row['Id'] not in ids]
        for key, value in previous.items():
            if key not in ('Attributes', 'Devices', 'Drugs'):
                data[key] = value
        if data == previous:
            print('GameContent.json is already complete; existing values preserved.')
            return
    output.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"Built {len(data['Attributes'])} attributes, {len(data['Devices'])} devices, {len(data['Drugs'])} drugs.")

if __name__ == '__main__': generate()
