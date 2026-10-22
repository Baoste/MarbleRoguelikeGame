"""Build the reviewed gameplay catalog. No third-party Excel dependency.

The dictionary supplies numerical attribute rules. Legacy drug rows and the
obsolete effect-parameter sheet are not imported.
"""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
import zipfile
from sync_mres_devices import read_devices

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
    output = ROOT/'Resources/GameContent.json'
    data = dict(Attributes=attrs, RushThreshold=100,
                OverkillCoinsPerScore=.01, CentimetersToUnits=.01, MaxSplitGeneration=2)
    # Read first: invalid packages abort before publishing any content changes.
    data['Devices'] = read_devices(ROOT/'Resources/Devices')
    if output.exists():
        previous = json.loads(output.read_text(encoding='utf-8-sig'))
        for key in ('Attributes',):
            existing = previous.get(key, [])
            ids = {row['Id'] for row in existing}
            data[key] = existing + [row for row in data[key] if row['Id'] not in ids]
        for key, value in previous.items():
            if key not in ('Attributes', 'Drugs', 'Devices'):
                data[key] = value
        if data == previous:
            print('GameContent.json is complete; Devices match current MRes packages.')
            return
    output.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"Built {len(data['Attributes'])} attributes and {len(data['Devices'])} devices.")

if __name__ == '__main__': generate()
