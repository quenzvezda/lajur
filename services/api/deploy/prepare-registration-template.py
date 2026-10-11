"""Build neutral onboarding input from the committed synthetic fixture only.

Usage: python prepare-registration-template.py fixture.xlsm output.xlsm
Year generation later clears synthetic rows through the existing verified adapter.
Never pass an owner's private workbook as this source.
"""
from pathlib import Path
import sys
import zipfile
import xml.etree.ElementTree as ET
import hashlib
import copy
import re

S = '{http://schemas.openxmlformats.org/spreadsheetml/2006/main}'
R = '{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'
APPROVED_FIXTURE_SHA256 = 'b636d6e9694ef3e628052ce054f7b9112331f522afe66331cf51fb4094d6d41a'


def prepare(source, target):
    if hashlib.sha256(source.read_bytes()).hexdigest() != APPROVED_FIXTURE_SHA256:
        raise ValueError('Only the approved synthetic fixture can prepare a registration template')
    with zipfile.ZipFile(source) as original:
        workbook = ET.fromstring(original.read('xl/workbook.xml'))
        relationships = ET.fromstring(original.read('xl/_rels/workbook.xml.rels'))
        targets = {item.attrib['Id']: item.attrib['Target'].lstrip('/') for item in relationships}
        patches = {}
        vba = original.read('xl/vbaProject.bin')
        for sheet in workbook.find(S+'sheets'):
            name = sheet.attrib['name']
            relative = targets[sheet.attrib[R+'id']]
            path = relative if relative.startswith('xl/') else 'xl/'+relative
            xml = original.read(path).decode('utf-8')
            def clear(match):
                opening, reference, inner, closing = match.groups()
                if name == 'Master':
                    reset = reference.startswith('C') and reference[1:].isdigit() and 5 <= int(reference[1:]) <= 81
                elif name in ('Jan','Feb','Mar','April','May','June','July','Aug','Sep','Okt','Nov','Des'):
                    reset = reference.startswith('M') and reference[1:].isdigit() and 2 <= int(reference[1:]) <= 33
                else:
                    reset = False
                numeric_type = not re.search(r'\bt="(?!n")[^"]+"', opening)
                if reset and numeric_type and not re.search(r'<(?:\w+:)?f(?:\s|>)', inner):
                    inner = re.sub(r'(<(?:\w+:)?v>)[^<]*(</(?:\w+:)?v>)', r'\g<1>0\g<2>', inner)
                return opening+inner+closing
            # Preserve all original namespaces, markup-compatibility aliases and
            # formatting. Only replace values in selected numeric literal cells.
            patched = re.sub(r'(<(?:\w+:)?c\b[^>]*\br="([^"]+)"[^>]*(?<!/)>)(.*?)(</(?:\w+:)?c>)', clear, xml, flags=re.DOTALL)
            if patched != xml:
                patches[path] = patched.encode('utf-8')
        target.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(target, 'x', compression=zipfile.ZIP_DEFLATED) as output:
            for entry in original.infolist():
                output.writestr(copy.copy(entry), patches.get(entry.filename, original.read(entry.filename)))
        with zipfile.ZipFile(target) as output:
            assert output.read('xl/vbaProject.bin') == vba
    print('NEUTRAL_SYNTHETIC_TEMPLATE_READY', hashlib.sha256(target.read_bytes()).hexdigest())


if __name__ == '__main__':
    prepare(Path(sys.argv[1]), Path(sys.argv[2]))
