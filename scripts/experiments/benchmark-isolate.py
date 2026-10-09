import io, json, os, pathlib, shutil, signal, statistics, subprocess, sys, tarfile, time, uuid

PREFIX = pathlib.Path('/opt/minos-isolate-phase2-20261008-8f185bb')
WORK = PREFIX / 'work'
ISO = str(PREFIX / 'isolate')
IMAGE = 'gcc@sha256:5e927c284bf55a7dc796262e311a0703344f62f41f5621eb56843111b1d37e15'
COMMIT = '8f185bb37f3f23e29b33b0c7727c91c13429abe3'
assert os.getuid() == 1000 and PREFIX.is_dir() and not PREFIX.is_symlink()
assert (PREFIX / '.owner').read_text().strip() == 'minos-phase2-' + COMMIT
assert shutil.rmtree.avoids_symlink_attacks

SUM = "#include <iostream>\nint main(){int a,b;std::cin>>a>>b;std::cout<<a+b<<'\\n';}"
CASES = r'''
#include <iostream>
#include <csignal>
#include <unistd.h>
#include <sys/socket.h>
#include <arpa/inet.h>
#include <fcntl.h>
#include <cerrno>
#include <cstdlib>
#include <fstream>
int main(){int mode;std::cin>>mode;
 if(mode==0){if(getuid()!=61000)return 2; const char* paths[]={"/var/run/docker.sock","/root/.ssh/id_rsa","/opt/minos-isolate-phase2-20261008-8f185bb/work/secret","/input/../secret","/sys/fs/cgroup"};for(auto p:paths)if(access(p,F_OK)==0)return 3;
 int s=socket(AF_INET,SOCK_STREAM,0);sockaddr_in a{};a.sin_family=AF_INET;a.sin_port=htons(80);inet_pton(AF_INET,"1.1.1.1",&a.sin_addr);fcntl(s,F_SETFL,O_NONBLOCK);if(connect(s,(sockaddr*)&a,sizeof(a))==0||errno!=ENETUNREACH)return 4;close(s);
 std::ofstream f("/input/main");if(f.good())return 5;std::cout<<"42\n";return 0;}
 if(mode==1){std::cout<<"wrong\n";return 0;}
 if(mode==2)return 7;
 if(mode==3){volatile unsigned long long n=0;for(;;)++n;}
 if(mode==4){for(;;)std::cout<<"01234567890123456789012345678901234567890123456789";}
 if(mode==5){for(int i=0;i<512;i++){auto p=(volatile char*)malloc(1024*1024);if(!p)return 9;for(int j=0;j<1024*1024;j+=4096)p[j]=1;}}
 if(mode==6){if(fork()==0){sleep(20);return 0;}std::cout<<"42\n";return 0;}
 if(mode==7){int rejected=0;for(int i=0;i<100;i++){int p=fork();if(p==0){sleep(20);return 0;}if(p<0)rejected++;}std::cout<<(rejected>0?"42\n":"bad\n");return 0;}
 if(mode==8){volatile unsigned long long n=0;for(int i=0;i<10000000;i++)n+=i;std::cout<<"42\n";return 0;}
 if(mode==9){sleep(5);std::cout<<"42\n";return 0;}
 if(mode==11){for(;;)std::cerr<<"01234567890123456789012345678901234567890123456789";}
 if(mode==12){signal(SIGXFSZ,SIG_IGN);for(;;)std::cout<<"01234567890123456789012345678901234567890123456789";}
 if(mode==10){if(fork()==0){volatile unsigned long long n=0;for(;;)++n;}sleep(20);}
}
'''

def command(args, **kwargs):
    result = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20, **kwargs)
    if result.returncode:
        raise RuntimeError(f'Control command failed ({result.returncode}): {result.stderr[:4096].decode(errors="replace")}')
    return result.stdout

def compile_fixture(source, name):
    directory = WORK / name
    directory.mkdir(mode=0o755)
    (directory / 'main.cpp').write_text(source)
    container = 'minos-isolate-compile-' + uuid.uuid4().hex
    build = ('g++ -std=c++20 -O2 -pipe /input/main.cpp -o /scratch/main && '
             'tar -chf - -C /scratch main -C /usr/local/lib64 libstdc++.so.6 libgcc_s.so.1 '
             '-C /lib/x86_64-linux-gnu libc.so.6 libm.so.6 ld-linux-x86-64.so.2')
    args = ['docker', 'run', '--name', container, '--network', 'none', '--cap-drop', 'ALL',
            '--security-opt', 'no-new-privileges', '--read-only', '--memory', '256m', '--memory-swap', '256m',
            '--cpus', '1', '--pids-limit', '34', '--user', '1000:1000', '--log-driver', 'none',
            '--env', 'TMPDIR=/scratch', '--env', 'HOME=/scratch', '--workdir', '/scratch',
            '--tmpfs', '/scratch:size=67108864,mode=700,uid=1000,gid=1000,nosuid,nodev,noexec',
            '--ulimit', 'cpu=9', '--ulimit', 'nofile=64:64', '--ulimit', 'core=0:0',
            '--mount', f'type=bind,src={directory},dst=/input,readonly', IMAGE, '/bin/sh', '-c', build]
    started = time.perf_counter()
    try:
        archive = command(args)
    finally:
        command(['docker', 'rm', '-f', container])
    seconds = time.perf_counter() - started
    # Trusted pinned-image export: reject links, paths and unexpected files rather than extracting a tar tree.
    allowed = {'main', 'libstdc++.so.6', 'libgcc_s.so.1', 'libc.so.6', 'libm.so.6', 'ld-linux-x86-64.so.2'}
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        assert set(member.name for member in tar) == allowed
        for member in tar:
            assert member.isfile() and member.size <= 32 * 1024 * 1024
            (directory / member.name).write_bytes(tar.extractfile(member).read())
            (directory / member.name).chmod(0o555)
    return directory, seconds

def run_fixture(directory, input_bytes, expected=b'42\n', cpu=1, wall=2, output_kib=8192, cancel_after=None):
    started = time.perf_counter()
    stage = WORK / ('stage-' + uuid.uuid4().hex)
    scratch = PREFIX / 'scratch' / stage.name
    stage.mkdir(mode=0o755)
    scratch.mkdir(mode=0o777)
    scratch.chmod(0o777)
    shutil.copyfile(directory / 'main', stage / 'main')
    (stage / 'main').chmod(0o555)
    (stage / 'stdin').write_bytes(input_bytes)
    meta = WORK / (stage.name + '.meta')
    initialized = False
    try:
        box = command([ISO, '--cg', '--box-id=0', '--init']).decode().strip()
        initialized = True
        args = [ISO, '--cg', '--box-id=0', '--cg-mem=65536', f'--time={cpu}', f'--wall-time={wall}',
                '--processes=32', '--open-files=64', '--core=0', f'--fsize={output_kib}', '--no-default-dirs',
                f'--dir=/box={box}', f'--dir=/input={stage}', f'--dir=/scratch={scratch}:rw:noexec',
                f'--dir=/lib64={directory}', f'--dir=/lib/x86_64-linux-gnu={directory}',
                f'--dir=/usr/local/lib64={directory}', '--dir=/proc=proc:fs', '--chdir=/scratch',
                f'--meta={meta}', '--stdin=/input/stdin', '--stdout=/scratch/out', '--stderr=/scratch/err',
                '--env=PATH=/usr/local/bin:/usr/bin:/bin', '--env=TMPDIR=/scratch', '--env=HOME=/scratch',
                '--run', '--', '/input/main']
        process = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        if cancel_after is not None:
            time.sleep(cancel_after)
            process.send_signal(signal.SIGINT)
        stdout, stderr = process.communicate(timeout=wall + 5)
        if process.returncode not in (0, 1):
            raise RuntimeError(f'Isolate infrastructure fault {process.returncode}: {stderr.decode(errors="replace")}')
        values = dict(line.split(':', 1) for line in meta.read_text().splitlines())
        assert all(key in values for key in ('time', 'time-wall', 'cg-mem')) and values.get('status') != 'XX'
        observed = (scratch / 'out').read_bytes()
        if cancel_after is not None: status = 'Cancelled'
        elif values.get('exitsig') == str(int(signal.SIGXFSZ)) and len(observed) >= output_kib * 1024: status = 'OutputLimitExceeded'
        elif values.get('cg-oom-killed') == '1': status = 'MemoryLimitExceeded'
        elif values.get('status') == 'TO': status = 'TimeLimitExceeded'
        elif values.get('exitcode') != '0': status = 'RuntimeError'
        elif observed != expected: status = 'WrongAnswer'
        else: status = 'Passed'
        return {'status': status, 'cpuSeconds': float(values['time']), 'wallSeconds': float(values['time-wall']),
                'memoryBytes': int(values['cg-mem']) * 1024, 'stdoutBytes': len(observed),
                'stderrBytes': (scratch / 'err').stat().st_size, 'metadata': values}
    finally:
        if initialized: command([ISO, '--cg', '--box-id=0', '--cleanup'])
        assert not (pathlib.Path((PREFIX / 'cgroup').read_text().strip()) / 'box-0').exists()
        shutil.rmtree(stage)
        shutil.rmtree(scratch)
        if meta.exists(): meta.unlink()

def main():
    sum_dir, compile_seconds = compile_fixture(SUM, 'sum-' + uuid.uuid4().hex)
    case_dir, fixture_compile_seconds = compile_fixture(CASES, 'cases-' + uuid.uuid4().hex)
    (WORK / 'secret').write_text('private-sentinel-not-mounted')
    cases = []
    for mode, expected in [(0,'Passed'),(1,'WrongAnswer'),(2,'RuntimeError'),(3,'TimeLimitExceeded'),
                           (4,'OutputLimitExceeded'),(5,'MemoryLimitExceeded'),(6,'Passed'),(7,'Passed'),
                           (8,'Passed'),(9,'Passed'),(10,'TimeLimitExceeded')]:
        started = time.perf_counter()
        row = run_fixture(case_dir, f'{mode}\n'.encode(), cpu=4 if mode==9 else .3, wall=8 if mode==9 else 3, output_kib=4)
        row.update(mode=mode, totalSeconds=time.perf_counter()-started)
        print('ISOLATE_FIXTURE=' + json.dumps(row), flush=True)
        assert row['status'] == expected, row
        if mode == 6: assert row['wallSeconds'] < 2
        if mode == 9: assert row['wallSeconds'] >= 5
        cases.append(row)
    cancel = run_fixture(case_dir, b'3\n', cpu=8, wall=10, cancel_after=.2)
    assert cancel['status'] == 'Cancelled'
    cases.append(cancel)
    durations, walls, cpus = [], [], []
    started = time.perf_counter()
    for index in range(100):
        timer = time.perf_counter()
        row = run_fixture(sum_dir, b'19 23\n')
        durations.append(time.perf_counter()-timer)
        assert row['status'] == 'Passed', row
        walls.append(row['wallSeconds']); cpus.append(row['cpuSeconds'])
    runtime_seconds = time.perf_counter() - started
    result = dict(measuredAtUtc=time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), sourceCommit=COMMIT,
                  compilerImage=IMAGE, fixture='trusted C++20 sum, 100 serial fresh Isolate cgroup sandboxes',
                  scope='native WSL prototype; excludes API/DB/queue/helper setup; compile includes runtime-library export but excludes Python unpacking; compiler CLI differs from official adapter',
                  compileAndExportSeconds=compile_seconds, runtimeSeconds=runtime_seconds,
                  runMedianSeconds=statistics.median(durations), runP95Seconds=sorted(durations)[94],
                  capturedExecutionWallSeconds=sum(walls), testCpuSeconds=sum(cpus), fixtures=cases,
                  perRunSeconds=durations)
    print('ISOLATE_BENCHMARK_JSON=' + json.dumps(result), flush=True)

def probe_gaps():
    directory, _ = compile_fixture(CASES, 'gaps-' + uuid.uuid4().hex)
    rows = []
    for name, mode, limit in [('stderr-limit', 11, 4), ('ignored-SIGXFSZ', 12, 4),
                              ('scratch-output-boundary', 4, 8192)]:
        row = run_fixture(directory, f'{mode}\n'.encode(), cpu=.3, wall=3, output_kib=limit)
        row.update(probe=name, requiredStatus='OutputLimitExceeded')
        row['matchesRequiredStatus'] = row['status'] == row['requiredStatus']
        # The first two expose gaps; also measure the actual scratch/output boundary.
        if mode in (11, 12): assert not row['matchesRequiredStatus'], row
        rows.append(row)
    print('ISOLATE_GAPS_JSON=' + json.dumps(rows), flush=True)


if __name__ == '__main__':
    if sys.argv[1:] == ['--probe-gaps']: probe_gaps()
    elif not sys.argv[1:]: main()
    else: raise SystemExit('Usage: benchmark-isolate.py [--probe-gaps]')
