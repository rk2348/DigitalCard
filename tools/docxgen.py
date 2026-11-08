# -*- coding: utf-8 -*-
"""標準ライブラリのみで提出用 .docx を組み立てる最小ヘルパー。

build_submission_documents.py から利用する。目次(TOC)はフィールドとして埋め込み、
Word で開いて「フィールドの更新」をすると(または tools/finalize_docx.ps1 を実行すると)ページ番号が入る。
"""
import re
import zipfile
from xml.sax.saxutils import escape

FONT = "游ゴシック"
FONT_LATIN = "Yu Gothic"
NAVY = "1F3864"
ACCENT = "2E75B6"
GRID = "BFC7D5"
ZEBRA = "F3F6FA"
KEYFILL = "E8EEF7"
CONTENT_W = 9638  # A4 幅 11906 - 左右余白 1134*2

CALLOUT_COLORS = {
    "info": ("2E75B6", "EAF2FB"),
    "warn": ("C55A11", "FDF1E7"),
    "decision": ("548235", "EEF5E9"),
    "summary": ("1F3864", "F2F4F8"),
}


def _runs(text, base_rpr=""):
    """**太字** を解釈して w:r の列にする。"""
    out = []
    for i, part in enumerate(re.split(r"\*\*", text)):
        if not part:
            continue
        rpr = base_rpr + ("<w:b/>" if i % 2 == 1 else "")
        for j, line in enumerate(part.split("\n")):
            if j:
                out.append(f"<w:r><w:rPr>{rpr}</w:rPr><w:br/></w:r>")
            if line:
                out.append(f'<w:r><w:rPr>{rpr}</w:rPr><w:t xml:space="preserve">{escape(line)}</w:t></w:r>')
    return "".join(out)


class Doc:
    def __init__(self, title, short_title, version, date):
        self.title = title
        self.short_title = short_title
        self.version = version
        self.date = date
        self.body = []
        self.lists = []  # (numId, abstractId)

    # ---------- ブロック要素 ----------
    def p(self, text, style=None, align=None, rpr="", spacing=None, keep_next=False):
        ppr = ""
        if style:
            ppr += f'<w:pStyle w:val="{style}"/>'
        if keep_next:
            ppr += "<w:keepNext/>"
        if spacing:
            ppr += spacing
        if align:
            ppr += f'<w:jc w:val="{align}"/>'
        self.body.append(f"<w:p><w:pPr>{ppr}</w:pPr>{_runs(text, rpr)}</w:p>")

    def h1(self, text, page_break=False):
        pb = "<w:pageBreakBefore/>" if page_break else ""
        self.body.append(f'<w:p><w:pPr><w:pStyle w:val="Heading1"/>{pb}</w:pPr>{_runs(text)}</w:p>')

    def h2(self, text):
        self.p(text, "Heading2")

    def h3(self, text):
        self.p(text, "Heading3")

    def text(self, text):
        self.p(text, "BodyText")

    def note(self, text):
        self.p(text, "Note")

    def page_break(self):
        self.body.append('<w:p><w:r><w:br w:type="page"/></w:r></w:p>')

    def _list(self, items, abstract_id):
        num_id = len(self.lists) + 1
        self.lists.append((num_id, abstract_id))
        for item in items:
            level = 0
            if isinstance(item, tuple):
                level, item = item
            self.body.append(
                f'<w:p><w:pPr><w:pStyle w:val="ListPara"/><w:numPr><w:ilvl w:val="{level}"/>'
                f'<w:numId w:val="{num_id}"/></w:numPr></w:pPr>{_runs(item)}</w:p>'
            )

    def bullets(self, items):
        self._list(items, 1)

    def numbered(self, items):
        self._list(items, 2)

    def table(self, header, rows, widths, kv=False, font_size=18, align=None):
        """header=None なら見出し行なし。kv=True なら1列目を項目列として塗る。"""
        assert sum(widths) == CONTENT_W, (sum(widths), header)
        align = align or []
        grid = "".join(f'<w:gridCol w:w="{w}"/>' for w in widths)
        border = f'w:val="single" w:sz="4" w:space="0" w:color="{GRID}"'
        x = (
            f'<w:tbl><w:tblPr><w:tblW w:w="{CONTENT_W}" w:type="dxa"/><w:tblLayout w:type="fixed"/>'
            f"<w:tblBorders><w:top {border}/><w:left {border}/><w:bottom {border}/><w:right {border}/>"
            f"<w:insideH {border}/><w:insideV {border}/></w:tblBorders>"
            '<w:tblCellMar><w:top w:w="60" w:type="dxa"/><w:left w:w="100" w:type="dxa"/>'
            '<w:bottom w:w="60" w:type="dxa"/><w:right w:w="100" w:type="dxa"/></w:tblCellMar>'
            f"</w:tblPr><w:tblGrid>{grid}</w:tblGrid>"
        )

        def cell(content, w, fill, bold=False, color=None, jc=None):
            rpr = f'<w:sz w:val="{font_size}"/><w:szCs w:val="{font_size}"/>'
            if bold:
                rpr += "<w:b/>"
            if color:
                rpr += f'<w:color w:val="{color}"/>'
            paras = content if isinstance(content, list) else [content]
            jcx = f'<w:jc w:val="{jc}"/>' if jc else ""
            ps = "".join(
                f'<w:p><w:pPr><w:pStyle w:val="TableText"/>{jcx}</w:pPr>{_runs(str(t), rpr)}</w:p>' for t in paras
            )
            shd = f'<w:shd w:val="clear" w:color="auto" w:fill="{fill}"/>' if fill else ""
            return f'<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/>{shd}<w:vAlign w:val="center"/></w:tcPr>{ps}</w:tc>'

        if header:
            x += '<w:tr><w:trPr><w:cantSplit/><w:tblHeader/></w:trPr>'
            x += "".join(cell(h, w, NAVY, True, "FFFFFF") for h, w in zip(header, widths))
            x += "</w:tr>"
        for r, row in enumerate(rows):
            x += "<w:tr><w:trPr><w:cantSplit/></w:trPr>"
            for c, (v, w) in enumerate(zip(row, widths)):
                if kv and c == 0:
                    x += cell(v, w, KEYFILL, True)
                else:
                    fill = ZEBRA if (header and r % 2 == 1 and not kv) else None
                    jc = align[c] if c < len(align) else None
                    x += cell(v, w, fill, jc=jc)
            x += "</w:tr>"
        x += "</w:tbl>"
        self.body.append(x)
        self.body.append('<w:p><w:pPr><w:pStyle w:val="Spacer"/></w:pPr></w:p>')

    def callout(self, kind, title, lines):
        bar, fill = CALLOUT_COLORS[kind]
        ppr_base = (
            f'<w:pBdr><w:left w:val="single" w:sz="24" w:space="8" w:color="{bar}"/></w:pBdr>'
            f'<w:shd w:val="clear" w:color="auto" w:fill="{fill}"/><w:ind w:left="200" w:right="120"/>'
        )
        all_lines = [("title", title)] + [("line", l) for l in lines]
        for i, (k, l) in enumerate(all_lines):
            last = i == len(all_lines) - 1
            sp = '<w:spacing w:before="0" w:after="{}" w:line="300" w:lineRule="auto"/>'.format(160 if last else 0)
            if k == "title":
                title_rpr = '<w:b/><w:color w:val="' + bar + '"/>'
                self.body.append(f"<w:p><w:pPr><w:keepNext/>{ppr_base}{sp}</w:pPr>{_runs(l, title_rpr)}</w:p>")
            else:
                self.body.append(f"<w:p><w:pPr>{ppr_base}{sp}</w:pPr>{_runs(l)}</w:p>")

    def toc(self):
        self.p("目次", "TOCHeading")
        self.body.append(
            '<w:p><w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r>'
            '<w:r><w:instrText xml:space="preserve"> TOC \\o "1-2" \\h \\z \\u </w:instrText></w:r>'
            '<w:r><w:fldChar w:fldCharType="separate"/></w:r>'
            '<w:r><w:t>（目次を表示するには、ここを右クリックして「フィールドの更新」を選択してください）</w:t></w:r>'
            '<w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>'
        )

    def cover(self, kicker, subtitle, info_rows):
        self.body.append('<w:p><w:pPr><w:spacing w:before="1600" w:after="0"/></w:pPr></w:p>')
        self.p(kicker, "CoverKicker")
        self.p(self.title, "CoverTitle")
        self.p(subtitle, "CoverSubtitle")
        self.body.append(
            f'<w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="18" w:space="1" w:color="{ACCENT}"/></w:pBdr>'
            '<w:spacing w:after="600"/></w:pPr></w:p>'
        )
        self.table(None, info_rows, [2200, CONTENT_W - 2200], kv=True, font_size=19)

    # ---------- パッケージ ----------
    def _styles(self):
        f = f'<w:rFonts w:ascii="{FONT_LATIN}" w:hAnsi="{FONT_LATIN}" w:eastAsia="{FONT}" w:cs="{FONT_LATIN}"/>'

        def ps(sid, name, ppr="", rpr="", based="Normal", extra=""):
            return (
                f'<w:style w:type="paragraph" w:styleId="{sid}"><w:name w:val="{name}"/><w:basedOn w:val="{based}"/>'
                f"{extra}<w:qFormat/><w:pPr>{ppr}</w:pPr><w:rPr>{rpr}</w:rPr></w:style>"
            )

        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
            f'<w:docDefaults><w:rPrDefault><w:rPr>{f}<w:sz w:val="21"/><w:szCs w:val="21"/>'
            '<w:lang w:val="en-US" w:eastAsia="ja-JP"/></w:rPr></w:rPrDefault>'
            '<w:pPrDefault><w:pPr><w:spacing w:after="80" w:line="300" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>'
            '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/>'
            '<w:pPr><w:snapToGrid w:val="0"/></w:pPr><w:rPr><w:color w:val="262626"/></w:rPr></w:style>'
            + ps("BodyText", "Body Text", '<w:spacing w:after="120"/><w:jc w:val="both"/>')
            + ps("Note", "Note", '<w:spacing w:after="120"/>', '<w:color w:val="595959"/><w:sz w:val="18"/>')
            + ps("ListPara", "List Paragraph", '<w:spacing w:after="60"/><w:ind w:left="420" w:hanging="280"/>')
            + ps("TableText", "Table Text", '<w:spacing w:before="0" w:after="0" w:line="264" w:lineRule="auto"/>')
            + ps("Spacer", "Spacer", '<w:spacing w:before="0" w:after="120" w:line="120" w:lineRule="exact"/>')
            + ps(
                "Heading1", "heading 1",
                f'<w:keepNext/><w:keepLines/><w:spacing w:before="360" w:after="160"/>'
                f'<w:pBdr><w:bottom w:val="single" w:sz="8" w:space="4" w:color="{NAVY}"/></w:pBdr><w:outlineLvl w:val="0"/>',
                f'<w:b/><w:color w:val="{NAVY}"/><w:sz w:val="32"/><w:szCs w:val="32"/>',
                extra='<w:next w:val="BodyText"/>',
            )
            + ps(
                "Heading2", "heading 2",
                f'<w:keepNext/><w:keepLines/><w:spacing w:before="240" w:after="100"/>'
                f'<w:pBdr><w:left w:val="single" w:sz="24" w:space="6" w:color="{ACCENT}"/></w:pBdr>'
                '<w:ind w:left="140"/><w:outlineLvl w:val="1"/>',
                f'<w:b/><w:color w:val="{NAVY}"/><w:sz w:val="25"/><w:szCs w:val="25"/>',
                extra='<w:next w:val="BodyText"/>',
            )
            + ps(
                "Heading3", "heading 3",
                '<w:keepNext/><w:spacing w:before="160" w:after="60"/><w:outlineLvl w:val="2"/>',
                '<w:b/><w:sz w:val="22"/>',
                extra='<w:next w:val="BodyText"/>',
            )
            + ps("TOCHeading", "TOC Heading", '<w:spacing w:after="200"/>',
                 f'<w:b/><w:color w:val="{NAVY}"/><w:sz w:val="32"/>')
            + ps("TOC1", "toc 1", '<w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9628"/></w:tabs><w:spacing w:before="80" w:after="0" w:line="264" w:lineRule="auto"/>', "<w:b/>")
            + ps("TOC2", "toc 2", '<w:tabs><w:tab w:val="right" w:leader="dot" w:pos="9628"/></w:tabs><w:spacing w:after="0" w:line="264" w:lineRule="auto"/><w:ind w:left="420"/>', '<w:sz w:val="20"/>')
            + ps("CoverKicker", "Cover Kicker", '<w:spacing w:after="120"/>',
                 f'<w:color w:val="{ACCENT}"/><w:sz w:val="24"/><w:b/>')
            + ps("CoverTitle", "Cover Title", '<w:spacing w:after="200" w:line="240" w:lineRule="auto"/>',
                 f'<w:b/><w:color w:val="{NAVY}"/><w:sz w:val="60"/><w:szCs w:val="60"/>')
            + ps("CoverSubtitle", "Cover Subtitle", '<w:spacing w:after="200"/>',
                 '<w:color w:val="404040"/><w:sz w:val="28"/>')
            + '<w:style w:type="character" w:styleId="Hyperlink"><w:name w:val="Hyperlink"/>'
            '<w:rPr><w:color w:val="262626"/></w:rPr></w:style>'
            "</w:styles>"
        )

    def _numbering(self):
        bullet_lvls = "".join(
            f'<w:lvl w:ilvl="{i}"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="{c}"/>'
            f'<w:lvlJc w:val="left"/><w:pPr><w:ind w:left="{420 + 420 * i}" w:hanging="280"/></w:pPr>'
            f'<w:rPr><w:color w:val="{ACCENT}"/></w:rPr></w:lvl>'
            for i, c in enumerate(["●", "－"])
        )
        num_lvls = (
            '<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/>'
            '<w:lvlJc w:val="left"/><w:pPr><w:ind w:left="420" w:hanging="360"/></w:pPr>'
            f'<w:rPr><w:b/><w:color w:val="{NAVY}"/></w:rPr></w:lvl>'
            '<w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="－"/>'
            '<w:lvlJc w:val="left"/><w:pPr><w:ind w:left="840" w:hanging="280"/></w:pPr></w:lvl>'
        )
        x = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
            f'<w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="hybridMultilevel"/>{bullet_lvls}</w:abstractNum>'
            f'<w:abstractNum w:abstractNumId="2"><w:multiLevelType w:val="hybridMultilevel"/>{num_lvls}</w:abstractNum>'
        )
        for num_id, abs_id in self.lists:
            x += (
                f'<w:num w:numId="{num_id}"><w:abstractNumId w:val="{abs_id}"/>'
                '<w:lvlOverride w:ilvl="0"><w:startOverride w:val="1"/></w:lvlOverride></w:num>'
            )
        return x + "</w:numbering>"

    def save(self, path):
        W = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"'
        R = 'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"'
        small = '<w:rPr><w:color w:val="7F7F7F"/><w:sz w:val="16"/></w:rPr>'
        header = (
            f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:hdr {W}><w:p><w:pPr>'
            f'<w:pBdr><w:bottom w:val="single" w:sz="4" w:space="4" w:color="{GRID}"/></w:pBdr>'
            '<w:tabs><w:tab w:val="right" w:pos="9638"/></w:tabs></w:pPr>'
            f'<w:r>{small}<w:t xml:space="preserve">ORISAMO　{escape(self.short_title)}</w:t></w:r>'
            f'<w:r>{small}<w:tab/><w:t xml:space="preserve">{escape(self.version)}　{escape(self.date)}</w:t></w:r>'
            "</w:p></w:hdr>"
        )

        def fld(instr):
            return (
                f'<w:r>{small}<w:fldChar w:fldCharType="begin"/></w:r>'
                f'<w:r>{small}<w:instrText xml:space="preserve"> {instr} </w:instrText></w:r>'
                f'<w:r>{small}<w:fldChar w:fldCharType="separate"/></w:r><w:r>{small}<w:t>1</w:t></w:r>'
                f'<w:r>{small}<w:fldChar w:fldCharType="end"/></w:r>'
            )

        footer = (
            f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:ftr {W}><w:p><w:pPr><w:jc w:val="center"/></w:pPr>'
            + fld("PAGE")
            + f'<w:r>{small}<w:t xml:space="preserve"> / </w:t></w:r>'
            + fld("NUMPAGES")
            + "</w:p></w:ftr>"
        )
        sect = (
            '<w:sectPr><w:headerReference w:type="default" r:id="rIdH"/><w:footerReference w:type="default" r:id="rIdF"/>'
            '<w:pgSz w:w="11906" w:h="16838"/>'
            '<w:pgMar w:top="1300" w:right="1134" w:bottom="1134" w:left="1134" w:header="600" w:footer="500" w:gutter="0"/>'
            '<w:titlePg/><w:docGrid w:linePitch="360"/></w:sectPr>'
        )
        document = (
            f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document {W} {R}><w:body>'
            + "".join(self.body)
            + sect
            + "</w:body></w:document>"
        )
        settings = (
            f'<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:settings {W}>'
            '<w:defaultTabStop w:val="420"/><w:characterSpacingControl w:val="compressPunctuation"/>'
            '<w:compat><w:compatSetting w:name="compatibilityMode" w:uri="http://schemas.microsoft.com/office/word" w:val="15"/></w:compat>'
            "</w:settings>"
        )
        ct = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            '<Default Extension="xml" ContentType="application/xml"/>'
            '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
            '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>'
            '<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>'
            '<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>'
            '<Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>'
            '<Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>'
            '<Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>'
            "</Types>"
        )
        rels = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>'
            '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>'
            "</Relationships>"
        )
        doc_rels = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rIdS" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
            '<Relationship Id="rIdN" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering" Target="numbering.xml"/>'
            '<Relationship Id="rIdT" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/>'
            '<Relationship Id="rIdH" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>'
            '<Relationship Id="rIdF" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>'
            "</Relationships>"
        )
        core = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
            'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
            f"<dc:title>{escape(self.title)}</dc:title><dc:creator>DigitalCard Project</dc:creator>"
            '<dcterms:created xsi:type="dcterms:W3CDTF">2026-09-29T00:00:00Z</dcterms:created></cp:coreProperties>'
        )
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr("[Content_Types].xml", ct)
            z.writestr("_rels/.rels", rels)
            z.writestr("word/_rels/document.xml.rels", doc_rels)
            z.writestr("word/document.xml", document)
            z.writestr("word/styles.xml", self._styles())
            z.writestr("word/numbering.xml", self._numbering())
            z.writestr("word/settings.xml", settings)
            z.writestr("word/header1.xml", header)
            z.writestr("word/footer1.xml", footer)
            z.writestr("docProps/core.xml", core)
