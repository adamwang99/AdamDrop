"""Tao tep AdamDrop.auto.shortcut — phim tat cho TU DONG HOA (Wi-Fi nha / theo gio).

Phim tat: lay anh/video chup trong ngay (moi nhat truoc) -> gui tung tep ve may tinh
voi ifnew=1 (may tinh bo qua tep da co, khong sinh (1)(2)(3)) -> bao thong bao.

Chay: python lam-shortcut-auto.py <host:port> <key>
"""
import plistlib, sys, uuid, os

HOST = sys.argv[1] if len(sys.argv) > 1 else ''
if not HOST:
    raise SystemExit('Thieu ten may: python tools/lam-shortcut-auto.py <ten-may.local>:8765 <ma-khoa>')
KEY = sys.argv[2] if len(sys.argv) > 2 else ''
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'web/AdamDrop.auto.shortcut')

URL = 'http://%s/upload?k=%s&ifnew=1' % (HOST, KEY)


def att(u, name, kind='ActionOutput'):
    return {'Value': {'OutputUUID': u, 'OutputName': name, 'Type': kind},
            'WFSerializationType': 'WFTextTokenAttachment'}


def token_att(u, name, kind='ActionOutput'):
    return {'Value': {'attachmentsByRange': {'{0, 1}': {
                'OutputUUID': u, 'OutputName': name, 'Type': kind}},
                'string': '\ufffc'},
            'WFSerializationType': 'WFTextTokenString'}


U_FIND = str(uuid.uuid4()).upper()
U_LOOP = str(uuid.uuid4()).upper()
G_LOOP = str(uuid.uuid4()).upper()

actions = [
    # 1. Tim anh/video chup trong ngay, moi nhat truoc
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.filter.photos',
        'WFWorkflowActionParameters': {
            'UUID': U_FIND,
            'WFContentItemFilter': {
                'Value': {
                    'WFActionParameterFilterPrefix': 1,
                    'WFContentPredicateBoundedDate': False,
                    'WFActionParameterFilterTemplates': [
                        {'Operator': 1002, 'Property': 'Date Taken', 'Removable': True},
                    ],
                },
                'WFSerializationType': 'WFContentPredicateTableTemplate',
            },
            'WFContentItemSortProperty': 'Date Taken',
            'WFContentItemSortOrder': 'Latest First',
        },
    },
    # 2. Lap qua tung anh/video
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.repeat.each',
        'WFWorkflowActionParameters': {
            'UUID': U_LOOP,
            'GroupingIdentifier': G_LOOP,
            'WFControlFlowMode': 0,
            'WFInput': att(U_FIND, 'Photos'),
        },
    },
    # 3. Gui tep len may tinh (ifnew=1: may tinh bo qua tep da co)
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.downloadurl',
        'WFWorkflowActionParameters': {
            'WFURL': URL,
            'WFHTTPMethod': 'POST',
            'WFHTTPBodyType': 'File',
            'WFRequestVariable': token_att(U_LOOP, 'Repeat Item'),
        },
    },
    # 4. Ket thuc vong lap
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.repeat.each',
        'WFWorkflowActionParameters': {
            'GroupingIdentifier': G_LOOP,
            'WFControlFlowMode': 1,
        },
    },
    # 5. Bao xong
    {
        'WFWorkflowActionIdentifier': 'is.workflow.actions.notification',
        'WFWorkflowActionParameters': {
            'WFNotificationActionTitle': 'AdamDrop',
            'WFNotificationActionBody': 'Ảnh mới trong ngày đã được gửi về máy tính',
        },
    },
]

wf = {
    'WFWorkflowClientVersion': '1302.1.3',
    'WFWorkflowClientRelease': '18.0',
    'WFWorkflowMinimumClientVersion': 1300,
    'WFWorkflowMinimumClientVersionString': '1300',
    'WFWorkflowName': 'AdamDrop-auto',
    'WFWorkflowIcon': {'WFWorkflowIconStartColor': 4282601983, 'WFWorkflowIconGlyphNumber': 61440},
    'WFWorkflowImportQuestions': [],
    'WFWorkflowHasOutputFallback': False,
    'WFWorkflowOutputContentItemClasses': [],
    'WFWorkflowInputContentItemClasses': [],
    'WFWorkflowTypes': [],
    'WFWorkflowActions': actions,
}

with open(OUT, 'wb') as f:
    plistlib.dump(wf, f, fmt=plistlib.FMT_BINARY)
print('ghi:', OUT, os.path.getsize(OUT), 'byte')
print('url:', URL)
