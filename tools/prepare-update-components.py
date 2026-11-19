"""Prepare immutable, individually reusable tool and prerequisite archives."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import zipfile


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def relative(name):
    if not isinstance(name, str) or not name or len(name.encode('utf-16-le')) > 480:
        raise ValueError('Invalid component path')
    for part in name.split('/'):
        stem = part.split('.')[0].upper()
        if (not part or part in ('.', '..') or part.endswith((' ', '.')) or
                re.search(r'[\\<>:"|?*\x00-\x1f]', part) or
                stem in {'CON', 'PRN', 'AUX', 'NUL'} or re.fullmatch(r'(COM|LPT)[1-9]', stem)):
            raise ValueError(f'Invalid component path: {name}')
    return name


def plain(path):
    for parent in (path, *path.parents):
        if parent.is_symlink() or parent.is_junction():
            raise ValueError(f'Component path contains a link: {path}')
    return path


def revision(files):
    return hashlib.sha256(json.dumps(files, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest()


def write_json(path, value):
    temporary = path.with_suffix('.json.partial')
    with temporary.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write('\n')
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def validate_component(component):
    identifier = component['id']
    if not re.fullmatch(r'[a-z0-9-]{1,80}', identifier):
        raise ValueError('Invalid component ID')
    seen = set()
    for file in component['files']:
        name = relative(file['path'])
        allowed = name.startswith(f'toolbox/files/{identifier[5:]}/') if identifier.startswith('tool-') else {
            'dependency-webview2': name == 'dependencies/Offline/WebView2.exe',
            'dependency-dotnet48': name == 'dependencies/Offline/DotNet48.exe',
            'dependency-pawnio': name.startswith('dependencies/PawnIO/'),
        }.get(identifier, False)
        if (not allowed or name.lower() in seen or not isinstance(file['length'], int) or
                not 0 <= file['length'] <= 2 * 1024**3 or not re.fullmatch(r'[a-f0-9]{64}', file['sha256'])):
            raise ValueError('Invalid component file identity')
        seen.add(name.lower())
    if not seen or component['revision'] != revision(sorted(component['files'], key=lambda file: file['path'])):
        raise ValueError('Invalid component inventory revision')
    asset = component['asset']
    if not re.fullmatch(r'v[2-9]\d*\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', asset['tag']):
        raise ValueError('Invalid component source tag')
    package = asset['package']
    expected = f'FrameScope-component-{identifier}-{component["revision"]}.zip'
    if package['name'] != expected or not 0 < package['size'] <= 2 * 1024**3 or not re.fullmatch(r'[a-f0-9]{64}', package['sha256']):
        raise ValueError('Invalid immutable component asset')


def pinned_components(root, previous):
    specifications = []
    lock = read_json(root / 'dependencies.lock.json')
    pawn = next(row for row in read_json(root / 'build-dependencies.lock.json')['files'] if row['file'] == 'packaging/PawnIO_setup.exe')
    for identifier, pin, destination in [
        ('dependency-webview2', lock['webView2StandaloneInstaller'], 'dependencies/Offline/WebView2.exe'),
        ('dependency-dotnet48', lock['dotNet48OfflineInstaller'], 'dependencies/Offline/DotNet48.exe'),
        ('dependency-pawnio', pawn, 'dependencies/PawnIO/PawnIO_setup.exe'),
    ]:
        source = root / relative(pin['file'])
        checksum = pin['sha256'].lower()
        old = previous.get(identifier)
        # An unchanged locked dependency uses the previous release's inventory;
        # cumulative builds need neither a local copy nor another compression.
        if old and len(old['files']) == 1 and old['files'][0]['path'] == destination and old['files'][0]['sha256'] == checksum:
            size = old['files'][0]['length']
        else:
            size = plain(source).stat().st_size
        specifications.append((identifier, [{'path': destination, 'length': size, 'sha256': checksum}], {destination: source}))
    if len({item[0] for item in specifications}) != len(specifications):
        raise ValueError('Duplicate component ID')
    return specifications


def prepare(root, output, tag, previous=None):
    root, output = Path(root).absolute(), Path(output).absolute()
    if not re.fullmatch(r'v[2-9]\d*\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', tag):
        raise ValueError('Invalid release tag')
    old = {}
    for component in (previous or {}).get('components', []):
        validate_component(component)
        if component['id'] in old:
            raise ValueError('Duplicate previous component')
        old[component['id']] = component
    specifications = pinned_components(root, old)
    output.mkdir(parents=True, exist_ok=True)
    plain(output)
    components, created = [], []
    for identifier, files, sources in specifications:
        identity = revision(files)
        if identifier in old and old[identifier]['revision'] == identity and old[identifier]['files'] == files:
            components.append(old[identifier])
            continue
        name = f'FrameScope-component-{identifier}-{identity}.zip'
        destination = output / name
        for file in files:
            source = plain(sources[file['path']])
            if source.stat().st_size != file['length'] or digest(source) != file['sha256']:
                raise ValueError(f'Pinned component changed: {file["path"]}')
        if destination.exists():
            # Never replace an already published/content-addressed archive.
            try:
                with zipfile.ZipFile(plain(destination)) as archive:
                    if sorted(archive.namelist()) != sorted(file['path'] for file in files):
                        raise ValueError('Existing immutable component has a different file set')
                    for file in files:
                        with archive.open(file['path']) as stream:
                            if archive.getinfo(file['path']).file_size != file['length'] or hashlib.file_digest(stream, 'sha256').hexdigest() != file['sha256']:
                                raise ValueError('Existing immutable component differs')
            except zipfile.BadZipFile as error:
                raise ValueError('Existing immutable component is invalid') from error
        else:
            temporary = destination.with_suffix('.zip.partial')
            with zipfile.ZipFile(temporary, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
                for file in files:
                    info = zipfile.ZipInfo(file['path'], date_time=(2026, 1, 1, 0, 0, 0))
                    info.compress_type = zipfile.ZIP_DEFLATED
                    info.external_attr = 0o100644 << 16
                    with plain(sources[file['path']]).open('rb') as source, archive.open(info, 'w') as target:
                        shutil.copyfileobj(source, target, 1024 * 1024)
            with zipfile.ZipFile(temporary) as archive:
                for file in files:
                    with archive.open(file['path']) as stream:
                        if archive.getinfo(file['path']).file_size != file['length'] or hashlib.file_digest(stream, 'sha256').hexdigest() != file['sha256']:
                            raise ValueError('Component changed during archive creation')
            temporary.rename(destination)
        component = {'id': identifier, 'revision': identity, 'asset': {'tag': tag, 'package': {
            'name': name, 'size': destination.stat().st_size, 'sha256': digest(destination)}}, 'files': files}
        validate_component(component)
        components.append(component)
        created.append(name)
    result = {'schema': 1, 'tag': tag, 'components': components, 'createdAssets': created}
    write_json(output / 'update-components.json', result)
    write_json(output / 'FrameScopeComponents.json', {'schema': 1, 'components': [
        {key: component[key] for key in ('id', 'revision', 'files')} for component in components]})
    return result


def release_manifests(output, catalog, version, git_sha, edition, cumulative=None, full=None, delivery='full'):
    """Prepare local release metadata only; signing and publication are separate."""
    if not re.fullmatch(r'[2-9]\d*\.\d+\.\d+(?:-beta\.[1-9]\d*)?', version) or not re.fullmatch(r'[a-f0-9]{40}', git_sha):
        raise ValueError('Invalid release version or source identity')
    if edition not in ('full', 'offline') or delivery not in ('full','cumulative') or (not '-' in version and full is None):
        raise ValueError('Invalid edition or missing stable full installer')
    if delivery=='full' and full is None:raise ValueError('Full delivery requires a full installer')
    if delivery=='cumulative' and cumulative is None:raise ValueError('Cumulative delivery requires an update installer')
    if catalog.get('schema') != 1 or catalog.get('tag') != 'v' + version:
        raise ValueError('Component catalog belongs to another release')
    seen, paths = set(), set()
    for component in catalog['components']:
        validate_component(component)
        if component['id'] in seen:
            raise ValueError('Duplicate release component')
        seen.add(component['id'])
        for file in component['files']:
            if file['path'].lower() in paths:
                raise ValueError('Overlapping release components')
            paths.add(file['path'].lower())
    def package(path, kind):
        path = plain(Path(path))
        suffix = '-offline' if edition == 'offline' else ''
        if path.name != f'FrameScope_{version}_x64{suffix}-{kind}.exe' or not 0 < path.stat().st_size <= 2 * 1024**3:
            raise ValueError('Installer identity does not match release')
        return {'name': path.name, 'size': path.stat().st_size, 'sha256': digest(path)}
    manifest = {'schema': 2, 'version': version, 'channel': 'beta' if '-' in version else 'stable',
                'git_sha': git_sha, 'platform': 'windows-x86_64', 'edition': edition,
                'cumulative': package(cumulative, 'update') if delivery=='cumulative' else None, 'full': package(full, 'setup') if full else None,
                'components': catalog['components']}
    output = plain(Path(output))
    output.mkdir(parents=True, exist_ok=True)
    manifests = {('offline-update-manifest-v2.json' if edition == 'offline' else 'update-manifest-v2.json'): manifest} if delivery=='cumulative' else {}
    if delivery=='full' and any((output/name).exists() for name in ('offline-update-manifest-v2.json','update-manifest-v2.json')):
        raise ValueError('Full delivery cannot coexist with a cumulative discovery manifest')
    if full:
        legacy = {key: manifest[key] for key in ('version', 'channel', 'git_sha', 'platform')}
        legacy = {'schema': 1, **legacy, 'installer': manifest['full']}
        if edition == 'full':
            manifests['release-manifest.json'] = legacy
            manifests['community-release-manifest.json'] = {**legacy, 'edition': 'full'}
        else:
            manifests['offline-release-manifest.json'] = {**legacy, 'edition': 'offline'}
    for name in manifests:
        if (output / name).exists():
            raise ValueError('Refusing to replace release metadata')
    for name, value in manifests.items():
        write_json(output / name, value)
    return manifest


def stage(root, resources, result, kind):
    """Stage only the component inventory for cumulative builds."""
    if kind not in ('full', 'cumulative'):
        raise ValueError('Invalid package kind')
    root, resources = Path(root).absolute(), plain(Path(resources).absolute())
    specifications = {identifier: (files, sources) for identifier, files, sources in pinned_components(
        root, {component['id']: component for component in result['components']})}
    if set(specifications) != {component['id'] for component in result['components']}:
        raise ValueError('Component catalog differs from current build inputs')
    for component in result['components']:
        validate_component(component)
        files, sources = specifications[component['id']]
        if component['files'] != files:
            raise ValueError('Component inventory differs from current build inputs')
        for file in files:
            destination = plain(resources / (file['path'] if file['path'].startswith('dependencies/') else 'backend/' + file['path']))
            if kind == 'cumulative':
                if destination.exists():
                    raise ValueError('Cumulative staging already contains a reusable component')
                continue
            source = plain(sources[file['path']])
            if source.stat().st_size != file['length'] or digest(source) != file['sha256']:
                raise ValueError(f'Pinned component changed: {file["path"]}')
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            if destination.stat().st_size != file['length'] or digest(destination) != file['sha256']:
                raise ValueError('Component changed during staging')
    backend = resources / 'backend'
    write_json(plain(backend / 'FrameScopeComponents.json'), {'schema': 1, 'components': [
        {key: component[key] for key in ('id', 'revision', 'files')} for component in result['components']]})
    write_json(plain(backend / 'FrameScopePackage.json'), {'schema': 1, 'kind': kind})


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--tag', required=True)
    parser.add_argument('--previous', type=Path)
    parser.add_argument('--stage', type=Path)
    parser.add_argument('--kind', choices=('full', 'cumulative'), default='full')
    args = parser.parse_args()
    result = prepare(args.root, args.output, args.tag, read_json(args.previous) if args.previous else None)
    if args.stage:
        stage(args.root, args.stage, result, args.kind)
    print(json.dumps({'components': len(result['components']), 'createdAssets': result['createdAssets']}))
