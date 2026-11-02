"""Fail closed on production markers; report paths and rule IDs, never secret text."""
import argparse
import io
import json
from pathlib import Path
import re
import zipfile

RULES={
    'private-key':re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'),
    'github-token':re.compile(rb'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,})'),
    'aws-key':re.compile(rb'\bAKIA[0-9A-Z]{16}\b'),
}
FORBIDDEN_PARTS={'server','certs','wheelhouse','backups','.ssh','.git','artifacts','output'}


def scan(entries,offline,markers):
    findings=[];count=0;unpacked=0
    def visit(name,data,depth=0):
        nonlocal count,unpacked
        count+=1
        for rule,pattern in RULES.items():
            if pattern.search(data):findings.append({'path':name,'rule':rule})
        for marker in markers:
            if marker.lower() in data.lower() or marker.decode().encode('utf-16le') in data:
                findings.append({'path':name,'rule':'production-marker'})
        local_name=name.split('!')[-1]
        path=Path(local_name);parts=set(path.parts)
        if parts&FORBIDDEN_PARTS or path.name.endswith('.credential.xml') or path.suffix.lower() in {'.pdb','.map','.sqlite3','.db','.key'}:
            findings.append({'path':name,'rule':'private-or-debug-artifact'})
        if offline and ('desktop-preview/src/community/' in name or path.name in {'account.rs','account_store.rs','community.rs','benchmark.rs'}):
            findings.append({'path':name,'rule':'online-client-source'})
        if offline and local_name.startswith(('src/', 'desktop-preview/src-tauri/src/', 'desktop-preview/src/')):
            symbols=[b'ExportBenchmark',b'report_benchmark_export',b'--export-benchmark',b'account::command',b'mod account;']
            if any(symbol in data for symbol in symbols):findings.append({'path':name,'rule':'online-runtime-entrypoint'})
        if depth<3:
            # ZIP payloads may be appended to a PE installer. ZipFile locates its central directory.
            try:archive=zipfile.ZipFile(io.BytesIO(data))
            except zipfile.BadZipFile:return
            with archive:
                for entry in archive.infolist():
                    if entry.is_dir():continue
                    unpacked+=entry.file_size
                    if entry.file_size>1024**3 or unpacked>4*1024**3:raise ValueError('Archive scan exceeds bounded size')
                    if '..' in Path(entry.filename).parts:raise ValueError('Unsafe archive path')
                    visit(name+'!'+entry.filename,archive.read(entry),depth+1)
    for name,data in entries:visit(name,data)
    return {'passed':not findings,'objects':count,'unpacked_bytes':unpacked,'findings':findings}


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('target',type=Path)
    parser.add_argument('--edition',choices=['offline','full'],required=True)
    parser.add_argument('--private-config',type=Path);parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args();markers=[]
    if args.private_config:
        from urllib.parse import urlsplit
        config=json.loads(args.private_config.read_text(encoding='utf-8-sig'))
        origin=urlsplit(config['account_origin']);markers.extend([origin.hostname.encode(),origin.netloc.encode()])
        markers.extend(str(value).encode() for value in config.get('scan_markers',[]) if len(str(value))>=5)
    if args.target.is_dir():
        entries=((str(p.relative_to(args.target)).replace('\\','/'),p.read_bytes()) for p in args.target.rglob('*') if p.is_file() and '.git' not in p.relative_to(args.target).parts)
    else:entries=[(args.target.name,args.target.read_bytes())]
    report=scan(entries,args.edition=='offline',markers)
    args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(json.dumps(report,indent=2),encoding='utf8')
    print(json.dumps(report));raise SystemExit(0 if report['passed'] else 1)
