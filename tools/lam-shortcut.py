"""Tao tep web/AdamDrop.shortcut (plist nhi phan) de cay vao bang chia se cua iPhone.

  python tools/lam-shortcut.py <host> <ma-khoa>
  vi du: python tools/lam-shortcut.py Adam-PC.local:8765 abc123xyz

LUU Y: iOS 26 chan viec nhap tep phim tat chua duoc Apple ky, nen cach nay chi con
dung cho may cu/hoc tap. Duong chinh la tu dung phim tat tren iPhone (xem tab Huong
dan trong app). Tep sinh ra CHUA MA KHOA cua ban - dung dua cho nguoi khac.
"""
import plistlib, sys, uuid, os

HOST = sys.argv[1] if len(sys.argv) > 1 else ''
if not HOST:
    raise SystemExit('Thieu ten may: python tools/lam-shortcut.py <ten-may.local>:8765 <ma-khoa>')
KEY = sys.argv[2] if len(sys.argv) > 2 else ''
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'web/AdamDrop.shortcut')

URL = 'http://%s/upload?k=%s' % (HOST, KEY)


def att(uuid_str, name, kind='ActionOutput'):
    """Tham chieu toi ket qua cua mot hanh dong (magic variable)."""
    return {
        'Value': {
            'OutputUUID': uuid_str,
            'OutputName': name,
            'Type': kind,
        },
        'WFSerializationType': 'WFTextTokenAttachment',
    }


def token_att(uuid_str, name, kind='ActionOutput'):
    """Chuoi co gan bien (o day chi co dung mot bien, khong co chu)."""
    return {
        'Value': {
            'attachmentsByRange': {'{0, 1}': {
                'OutputUUID': uuid_str, 'OutputName': name, 'Type': kind}},
            'string': '\ufffc',
        },
        'WFSerializationType': 'WFTextTokenString',
    }


U_LOOP = str(uuid.uuid4()).upper()
U_IN = str(uuid.uuid4()).upper()
G_LOOP = str(uuid.uuid4()).upper()

actions = [
    # 1. Lap qua tung tep duoc chia se
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.repeat.each',
        'WFWorkflowActionParameters': {
            'UUID': U_LOOP,
            'GroupingIdentifier': G_LOOP,
            'WFControlFlowMode': 0,
            'WFInput': att(U_IN, 'Shortcut Input', 'ExtensionInput'),
        },
    },
    # 2. Gui tep len may tinh
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.downloadurl',
        'WFWorkflowActionParameters': {
            'WFURL': URL,
            'WFHTTPMethod': 'POST',
            'WFHTTPBodyType': 'File',
            'WFRequestVariable': token_att(U_LOOP, 'Repeat Item'),
        },
    },
    # 3. Ket thuc vong lap
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.repeat.each',
        'WFWorkflowActionParameters': {
            'GroupingIdentifier': G_LOOP,
            'WFControlFlowMode': 1,
        },
    },
    # 4. Bao xong
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.notification',
        'WFWorkflowActionParameters': {
            'WFNotificationActionTitle': 'AdamDrop',
            'WFNotificationActionBody': 'Đã gửi sang máy tính',
        },
    },
]

wf = {
    'WFWorkflowClientVersion': '1302.1.3',
    'WFWorkflowClientRelease': '18.0',
    'WFWorkflowMinimumClientVersion': 1300,
    'WFWorkflowMinimumClientVersionString': '1300',
    'WFWorkflowName': 'AdamDrop',
    'WFWorkflowIcon': {'WFWorkflowIconStartColor': 4282601983, 'WFWorkflowIconGlyphNumber': 61440},
    'WFWorkflowImportQuestions': [],
    'WFWorkflowHasOutputFallback': False,
    'WFWorkflowOutputContentItemClasses': [],
    'WFWorkflowInputContentItemClasses': [
        'WFImageContentItem', 'WFFileContentItem', 'WFMediaContentItem',
        'WFURLContentItem', 'WFTextContentItem',
    ],
    'WFWorkflowTypes': ['ActionExtension'],
    'WFWorkflowActions': actions,
}

with open(OUT, 'wb') as f:
    plistlib.dump(wf, f, fmt=plistlib.FMT_BINARY)
print('ghi:', OUT, os.path.getsize(OUT), 'byte')
print('url:', URL)
