import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('neutral', Path(__file__).with_name('prepare-registration-template.py'))
neutral = importlib.util.module_from_spec(spec)
spec.loader.exec_module(neutral)


class NeutralTemplateTests(unittest.TestCase):
    def test_only_literal_defaults_change_and_all_other_parts_survive(self):
        source = Path(__file__).resolve().parents[3]/'tests/workbook-fixtures/LajurWorkbookFixture.xlsm'
        before_hash = source.read_bytes()
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory)/'registration.xlsm'
            neutral.prepare(source, target)
            with zipfile.ZipFile(source) as original, zipfile.ZipFile(target) as prepared:
                self.assertEqual(original.namelist(), prepared.namelist())
                changed = [name for name in original.namelist() if original.read(name) != prepared.read(name)]
                self.assertTrue(changed)
                for name in changed:
                    self.assertTrue(name.startswith('xl/worksheets/sheet') and name.endswith('.xml'))
                    old, new = ET.fromstring(original.read(name)), ET.fromstring(prepared.read(name))
                    old_cells = {cell.attrib['r']: cell for cell in old.iter(neutral.S+'c')}
                    for cell in new.iter(neutral.S+'c'):
                        previous = old_cells[cell.attrib['r']]
                        if ET.tostring(previous) != ET.tostring(cell):
                            self.assertIsNone(cell.find(neutral.S+'f'))
                            self.assertEqual('0', cell.find(neutral.S+'v').text)
                            self.assertEqual(previous.attrib, cell.attrib)
                self.assertEqual(original.read('xl/vbaProject.bin'), prepared.read('xl/vbaProject.bin'))
                self.assertEqual(original.read('xl/sharedStrings.xml'), prepared.read('xl/sharedStrings.xml'))
        self.assertEqual(before_hash, source.read_bytes())


if __name__ == '__main__':
    unittest.main()
