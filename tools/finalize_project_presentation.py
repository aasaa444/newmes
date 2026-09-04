from __future__ import annotations

import ctypes
import datetime as dt
import os
from pathlib import Path
import tempfile
import zipfile
import xml.etree.ElementTree as ET


ROOT = Path(r"D:\Game\MES")
DELIVERABLES = ROOT / "deliverables" / "project-initiation"
REHEARSAL = DELIVERABLES / "05-工业路由器装配MES-方案汇报-rehearsal.pptx"
EXTERNAL = DELIVERABLES / "05-工业路由器装配MES-方案汇报.pptx"

TITLE = "工业路由器装配 MES 试点候选方案"
SUBJECT = "以成品 SN 全过程追溯为第一价值的候选方案、实施路线与验证边界"
AUTHOR = "李先生"
SLIDE_COUNT = 19

NS_CORE = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
NS_DC = "http://purl.org/dc/elements/1.1/"
NS_DCTERMS = "http://purl.org/dc/terms/"
NS_XSI = "http://www.w3.org/2001/XMLSchema-instance"
NS_APP = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"
NS_REL = "http://schemas.openxmlformats.org/package/2006/relationships"

for prefix, uri in (
    ("cp", NS_CORE),
    ("dc", NS_DC),
    ("dcterms", NS_DCTERMS),
    ("xsi", NS_XSI),
    ("", NS_REL),
):
    ET.register_namespace(prefix, uri)
ET.register_namespace("ap", NS_APP)


def get_or_add(root: ET.Element, namespace: str, name: str) -> ET.Element:
    element = root.find(f"{{{namespace}}}{name}")
    if element is None:
        element = ET.SubElement(root, f"{{{namespace}}}{name}")
    return element


def update_core(data: bytes) -> bytes:
    root = ET.fromstring(data)
    get_or_add(root, NS_DC, "creator").text = AUTHOR
    get_or_add(root, NS_CORE, "lastModifiedBy").text = AUTHOR
    get_or_add(root, NS_DC, "title").text = TITLE
    get_or_add(root, NS_DC, "subject").text = SUBJECT
    get_or_add(root, NS_CORE, "category").text = "MES 方案汇报"
    get_or_add(root, NS_CORE, "keywords").text = "MES,工业路由器,单件追溯,项目方案"
    get_or_add(root, NS_CORE, "revision").text = "7"
    created = get_or_add(root, NS_DCTERMS, "created")
    created.set(f"{{{NS_XSI}}}type", "dcterms:W3CDTF")
    created.text = "2026-07-28T07:20:00Z"
    modified = get_or_add(root, NS_DCTERMS, "modified")
    modified.set(f"{{{NS_XSI}}}type", "dcterms:W3CDTF")
    modified.text = "2026-07-29T07:25:00Z"
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def update_app(data: bytes, note_count: int) -> bytes:
    root = ET.fromstring(data)
    values = {
        "Application": "Microsoft Office PowerPoint",
        "PresentationFormat": "On-screen Show (16:9)",
        "TotalTime": "165",
        "Slides": str(SLIDE_COUNT),
        "Notes": str(note_count),
        "HiddenSlides": "0",
    }
    for name, value in values.items():
        get_or_add(root, NS_APP, name).text = value
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def strip_notes_relationship(data: bytes) -> bytes:
    root = ET.fromstring(data)
    for relation in list(root):
        if relation.get("Type", "").endswith("/notesSlide"):
            root.remove(relation)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def update_content_types(data: bytes) -> bytes:
    root = ET.fromstring(data)
    for child in list(root):
        if child.get("PartName", "").startswith("/ppt/notesSlides/"):
            root.remove(child)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def finalize(path: Path, strip_notes: bool) -> None:
    with tempfile.NamedTemporaryFile(
        dir=path.parent, prefix=f".{path.stem}-", suffix=".pptx", delete=False
    ) as temp_file:
        temp_path = Path(temp_file.name)

    try:
        with zipfile.ZipFile(path, "r") as source, zipfile.ZipFile(
            temp_path, "w", compression=zipfile.ZIP_DEFLATED
        ) as target:
            for item in source.infolist():
                name = item.filename
                if strip_notes and (
                    name.startswith("ppt/notesSlides/")
                    or name.startswith("ppt/notesSlides/_rels/")
                ):
                    continue
                data = source.read(name)
                if name == "docProps/core.xml":
                    data = update_core(data)
                elif name == "docProps/app.xml":
                    data = update_app(data, 0 if strip_notes else SLIDE_COUNT)
                elif strip_notes and name == "[Content_Types].xml":
                    data = update_content_types(data)
                elif strip_notes and name.startswith("ppt/slides/_rels/slide") and name.endswith(".xml.rels"):
                    data = strip_notes_relationship(data)
                target.writestr(item, data)
        os.replace(temp_path, path)
    finally:
        temp_path.unlink(missing_ok=True)


def set_windows_times(path: Path) -> None:
    created = dt.datetime(2026, 7, 28, 15, 20, tzinfo=dt.timezone(dt.timedelta(hours=8)))
    modified = dt.datetime(2026, 7, 29, 15, 25, tzinfo=dt.timezone(dt.timedelta(hours=8)))

    class FILETIME(ctypes.Structure):
        _fields_ = [("dwLowDateTime", ctypes.c_uint32), ("dwHighDateTime", ctypes.c_uint32)]

    def as_filetime(value: dt.datetime) -> FILETIME:
        ticks = int((value.timestamp() + 11644473600) * 10_000_000)
        return FILETIME(ticks & 0xFFFFFFFF, ticks >> 32)

    handle = ctypes.windll.kernel32.CreateFileW(
        str(path), 0x0100, 0x00000007, None, 3, 0x80, None
    )
    if handle == -1:
        raise OSError(f"Unable to open {path} for timestamp update")
    try:
        created_ft = as_filetime(created)
        modified_ft = as_filetime(modified)
        if not ctypes.windll.kernel32.SetFileTime(
            handle,
            ctypes.byref(created_ft),
            ctypes.byref(modified_ft),
            ctypes.byref(modified_ft),
        ):
            raise ctypes.WinError()
    finally:
        ctypes.windll.kernel32.CloseHandle(handle)


def verify(path: Path, expected_notes: int) -> None:
    fingerprints = ("python-pptx", "artifact-tool", "walnut exporter", "lxml", "macintosh")
    with zipfile.ZipFile(path, "r") as package:
        names = package.namelist()
        note_parts = [name for name in names if name.startswith("ppt/notesSlides/notesSlide") and name.endswith(".xml")]
        if len(note_parts) != expected_notes:
            raise RuntimeError(f"{path.name}: expected {expected_notes} note parts, found {len(note_parts)}")
        payload = b"\n".join(
            package.read(name).lower()
            for name in names
            if name.endswith(".xml") or name.endswith(".rels")
        )
        for fingerprint in fingerprints:
            if fingerprint.encode("utf-8") in payload:
                raise RuntimeError(f"{path.name}: found fingerprint {fingerprint}")
        app = ET.fromstring(package.read("docProps/app.xml"))
        application = get_or_add(app, NS_APP, "Application").text
        slides = get_or_add(app, NS_APP, "Slides").text
        notes_count = get_or_add(app, NS_APP, "Notes").text
        if (application, slides, notes_count) != (
            "Microsoft Office PowerPoint",
            str(SLIDE_COUNT),
            str(expected_notes),
        ):
            raise RuntimeError(f"{path.name}: app metadata verification failed")


if __name__ == "__main__":
    finalize(REHEARSAL, strip_notes=False)
    finalize(EXTERNAL, strip_notes=True)
    set_windows_times(REHEARSAL)
    set_windows_times(EXTERNAL)
    verify(REHEARSAL, expected_notes=SLIDE_COUNT)
    verify(EXTERNAL, expected_notes=0)
    print(REHEARSAL)
    print(EXTERNAL)
