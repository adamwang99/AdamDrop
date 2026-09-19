"""Isolated integration test; no real config, receive folder or history is used."""
from pathlib import Path
import subprocess, os, json, time, urllib.request, urllib.error, urllib.parse, hashlib, tempfile, shutil, sys
ROOT=Path(__file__).resolve().parents[1]
RUN=Path(tempfile.mkdtemp(prefix='adamdrop-sharing-'))
CSC=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
def compile_app(out, test=False):
    args=[str(CSC),'/nologo','/codepage:65001','/target:'+('exe' if test else 'winexe'),'/out:'+str(out),'/r:System.Windows.Forms.dll','/r:System.Drawing.dll']
    for f in (ROOT/'web').rglob('*'):
        if f.is_file() and (f.parent==ROOT/'web' and f.suffix in ('.html','.js','.png','.ico','.shortcut') or f.parent.name=='guide' and f.suffix=='.png'):
            args.append('/resource:'+str(f)+',web.'+str(f.relative_to(ROOT/'web')).replace('\\','.').replace('/','.'))
    args.append(str(ROOT/'AdamDrop.cs'))
    if test: args+=['/main:AdamDrop.ShareTest',str(ROOT/'tests/share-test.cs')]
    subprocess.run(args,check=True)
BASE='http://localhost:8765'
def req(path,method='GET',headers=None,data=None):
    r=urllib.request.Request(BASE+path,data=data,method=method,headers=headers or {})
    try:
        with urllib.request.urlopen(r,timeout=10) as f:return f.status,f.headers,f.read()
    except urllib.error.HTTPError as f:return f.code,f.headers,f.read()
def check(ok,label):
    assert ok,label
    print('PASS',label,flush=True)
if __name__=='__main__':
    import socket
    with socket.socket() as probe:
        if probe.connect_ex(('127.0.0.1',8765)) == 0:
            sys.exit('REFUSED: port 8765 is in use. Stop the normal app only after checking active transfers; restore it after tests.')
    compile_app(RUN/'ShareTest.exe',True)
    p=subprocess.Popen([str(RUN/'ShareTest.exe')],stdout=subprocess.DEVNULL)
    try:
        for _ in range(100):
            if (RUN/'test-ready').exists():break
            if p.poll() is not None:raise RuntimeError('isolated server exited')
            time.sleep(.1)
        s=json.loads(req('/api/state')[2]); key=urllib.parse.quote(s['key']); token=s['adminToken']; auth={'X-AdamDrop-Admin':token}
        files=json.loads(req('/shares?k='+key)[2])['files']; check(len(files)==7,'seven explicitly selected fixtures')
        check(all(set(f)=={'id','name','size','mime'} for f in files),'queue hides PC paths')
        f=files[0]; url='/download?k='+key+'&id='+f['id']; expected=(RUN/f['name']).read_bytes()
        code,h,b=req(url);check(code==200 and b==expected,'full download bytes SHA256 '+hashlib.sha256(b).hexdigest())
        check("filename*=UTF-8''"+urllib.parse.quote(f['name']) in h['Content-Disposition'],'UTF8 filename and extension')
        for rng,segment in [('bytes=3-57',expected[3:58]),('bytes=-19',expected[-19:]),('bytes=1048570-',expected[1048570:])]:
            code,h,b=req(url,headers={'Range':rng});check(code==206 and b==segment and h['Content-Range'].startswith('bytes '),'range '+rng)
        for rng in ['bytes=9999999-','bytes=-0','bytes=8-2','bytes=0-1,3-4','bytes=oops']:
            code=req(url,headers={'Range':rng})[0];check(code in (400,416),'invalid range '+rng+' rejected '+str(code))
        code,h,b=req(url,headers={'Range':'bytes=0-1','If-Range':'"old"'});check(code==200 and b==expected,'If-Range falls back to full response')
        check(req(url,'HEAD')[2]==b'','HEAD no body')
        for item in files[1:5]:
            u='/download?k='+key+'&id='+item['id']+'&view=1';code,h,b=req(u)
            check(code==200 and b==(RUN/item['name']).read_bytes(),'fixture '+item['name'])
            check(h['X-Content-Type-Options']=='nosniff','nosniff '+item['name'])
            check(h['Content-Disposition'].startswith('inline' if item['mime'].startswith('image/') else 'attachment'),'safe preview '+item['name'])
        # Safari tren iOS ep TAI VE khi phan hoi co CSP `sandbox` => bam "Luu vao Anh" lai ra tep trong Tep.
        png=next(i for i in files if i['name']=='photo.png')
        latin=next(i for i in files if i['name']=='ảnh thử + test.tệp')
        hin=req('/download?k='+key+'&id='+png['id']+'&view=1')[1]
        check('sandbox' not in (hin.get('Content-Security-Policy') or ''),'inline preview carries no CSP sandbox header')
        check(hin['Content-Disposition']=='inline','inline preview sends bare inline without a filename')
        hat=req('/download?k='+key+'&id='+latin['id'])[1]
        check('sandbox' not in (hat.get('Content-Security-Policy') or ''),'attachment carries no CSP sandbox header')
        check(hat['Content-Disposition'].count('"')==2,'ASCII filename fallback stays quote-safe for Unicode names')
        check(req('/shares?k=wrong')[0]==403 and req('/download?k=wrong&id='+f['id'])[0]==403,'wrong key denied')
        # /list: danh sach link tai, moi tep 1 dong, cho Phim tat iOS (Lay noi dung cua URL -> Tach dong)
        code,h,body=req('/list?k='+key)
        rows=[r for r in body.decode().split('\n') if r.strip()]
        check(code==200 and len(rows)==7,'/list returns one absolute link per shared file')
        check(all(r.startswith('http://') and '/download?k=' in r and '&id=' in r for r in rows),'/list rows are absolute download links')
        check(req('/list?k=wrong')[0]==403,'/list wrong key denied')
        check(req('/api/state',headers={'Host':'evil.example:8765'})[0]==403,'DNS rebinding host denied')
        check(req('/api/share/stop')[0]==405,'admin GET denied')
        check(req('/api/share/stop','POST')[0]==403,'admin missing token denied')
        check(req('/api/share/stop','POST',dict(auth,Origin='https://evil.example'))[0]==403,'cross-origin mutation denied')
        code,h,b=req('/upload?k='+key+'&name=regression.bin','POST',{'Content-Type':'application/octet-stream'},expected)
        check(code==200 and (RUN/'received/regression.bin').read_bytes()==expected,'existing raw upload regression')
        code,h,b=req('/api/share/remove?id='+f['id'],'POST',auth);check(code==200 and req(url)[0]==404,'removal revokes download')
        check(len(json.loads(req('/shares?k='+key)[2])['files'])==6,'removal readback')
        missing=next(item for item in files if item['name']=='missing.bin')
        (RUN/'missing.bin').rename(RUN/'moved.bin')
        check(req('/download?k='+key+'&id='+missing['id'])[0]==410,'moved source reports unavailable')
        large=next(item for item in files if item['name']=='large.bin')
        streaming=urllib.request.urlopen(BASE+'/download?k='+key+'&id='+large['id'],timeout=10)
        transferred=len(streaming.read(1024))
        check(json.loads(req('/api/state')[2])['activeDownloads']>0,'large stream active before stop')
        req('/api/share/stop','POST',auth)
        import http.client
        try:
            while True:
                chunk=streaming.read(65536)
                if not chunk:break
                transferred+=len(chunk)
        except (http.client.IncompleteRead, ConnectionResetError, urllib.error.URLError) as error:
            transferred+=len(getattr(error,'partial',b''))
        finally:streaming.close()
        check(transferred<large['size'],'stop aborts active stream before full content')
        check(json.loads(req('/shares?k='+key)[2])['files']==[],'stop sharing readback')
        for _ in range(21):code,_,_=req('/shares?k=wrong')
        check(code==429,'sharing route wrong-key throttling')
        print('ALL SHARING TESTS PASSED; isolated artifacts:',RUN)
    finally:
        if p is not None:p.terminate();p.wait()
