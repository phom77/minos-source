#include <algorithm>
#include <atomic>
#include <cerrno>
#include <cstdlib>
#include <cstring>
#include <dirent.h>
#include <fcntl.h>
#include <fstream>
#include <iostream>
#include <netinet/in.h>
#include <string>
#include <sys/socket.h>
#include <sys/wait.h>
#include <thread>
#include <unistd.h>
#include <vector>

int main() {
    std::string mode;
    std::cin >> mode;
    if (mode == "ok") { std::cout << "ok\n"; return 0; }
    if (mode == "workspace") {
        std::string marker; std::cin >> marker;
        if (access("/tmp/minos-m1-execution", F_OK) == 0) return 18;
        DIR* input = opendir("/input"); if (!input) return 19;
        unsigned files = 0;
        while (dirent* entry = readdir(input)) {
            std::string name(entry->d_name);
            if (name == "." || name == "..") continue;
            if (name != "main" && name != "stdin.txt") return 19;
            ++files;
        }
        closedir(input); if (files != 2) return 19;
        usleep(300000); std::cout << marker << '\n'; return 0;
    }
    if (mode == "runtime") return 17;
    if (mode == "wall") for (;;) pause();
    if (mode == "cpu") { volatile unsigned long n = 0; for (;;) n = n * 17 + 1; }
    if (mode == "cpu-children") {
        for (unsigned i = 0; i < 3; ++i) {
            pid_t child = fork(); if (child == -1) return 20;
            if (child == 0) { volatile unsigned long n = 0; for (;;) n = n * 17 + 1; }
        }
        for (;;) pause();
    }
    if (mode == "memory") {
        std::vector<void*> blocks;
        for (;;) {
            void* block = std::malloc(4 * 1024 * 1024);
            if (!block) return 21;
            std::memset(block, 7, 4 * 1024 * 1024);
            blocks.push_back(block);
        }
    }
    if (mode == "stdout" || mode == "stderr") {
        char buffer[1024]; std::memset(buffer, 'x', sizeof(buffer));
        int fd = mode == "stdout" ? STDOUT_FILENO : STDERR_FILENO;
        for (;;) if (write(fd, buffer, sizeof(buffer)) < 0) return 22;
    }
    if (mode == "scratch") {
        char block[4096]; std::memset(block, 's', sizeof(block));
        unsigned long total = 0;
        for (unsigned file = 0; file < 256; ++file) {
            std::string name = "/scratch/file-" + std::to_string(file);
            int fd = open(name.c_str(), O_CREAT | O_WRONLY, 0600);
            if (fd < 0) return 23;
            for (unsigned part = 0; part < 256; ++part) {
                auto written = write(fd, block, sizeof(block));
                if (written < 0) {
                    int failure = errno; close(fd);
                    std::cout << "scratch_bytes=" << total << " errno=" << failure << '\n';
                    return failure == ENOSPC ? 43 : 24;
                }
                total += written;
            }
            close(fd);
        }
        return 25;
    }
    if (mode == "fork") {
        std::vector<pid_t> children;
        int failure = 0;
        for (unsigned i = 0; i < 128; ++i) {
            pid_t child = fork();
            if (child == -1) { failure = errno; break; }
            if (child == 0) for (;;) pause();
            children.push_back(child);
        }
        for (pid_t child : children) kill(child, SIGKILL);
        for (pid_t child : children) waitpid(child, nullptr, 0);
        std::cout << "children=" << children.size() << " errno=" << failure << '\n';
        return failure == EAGAIN && !children.empty() && children.size() < 32 ? 42 : 26;
    }
    if (mode == "threads") {
        std::atomic<bool> done(false);
        std::vector<std::thread> threads;
        bool denied = false;
        try {
            for (unsigned i = 0; i < 128; ++i)
                threads.emplace_back([&done] { while (!done) usleep(10000); });
        } catch (const std::system_error&) { denied = true; }
        done = true;
        for (auto& thread : threads) thread.join();
        std::cout << "threads=" << threads.size() << '\n';
        return denied && !threads.empty() && threads.size() < 32 ? 42 : 27;
    }
    if (mode == "children" || mode == "crash") {
        for (unsigned i = 0; i < 3; ++i) {
            pid_t child = fork();
            if (child == -1) return 28;
            if (child == 0) for (;;) pause();
        }
        if (mode == "crash") for (;;) pause();
        std::cout << "children_created=3\n";
        return 0;
    }
    if (mode == "network") {
        int fd = socket(AF_INET, SOCK_STREAM, 0);
        if (fd < 0) return 29;
        sockaddr_in destination{}; destination.sin_family = AF_INET;
        destination.sin_port = htons(53); destination.sin_addr.s_addr = htonl(0x01010101);
        int result = connect(fd, reinterpret_cast<sockaddr*>(&destination), sizeof(destination));
        int failure = errno; close(fd);
        std::cout << "connect_errno=" << failure << '\n';
        return result == -1 && failure == ENETUNREACH ? 0 : 30;
    }
    if (mode == "isolation") {
        if (getuid() != 1000 || getenv("MINOS_M1_SECRET")) return 31;
        for (const char* path : {"/mnt/d", "/host_mnt/d", "/mnt/host/d", "/repo", "/var/run/docker.sock",
                "/tmp/minos-m1-private-expected-answer.txt", "/input/expected.txt", "/input/next-test.txt", "/input/main.cpp"})
            if (access(path, F_OK) == 0) return 32;
        std::ifstream status("/proc/self/status");
        std::string line; bool caps = false, nnp = false, seccomp = false;
        while (std::getline(status, line)) {
            if (line == "CapEff:\t0000000000000000") caps = true;
            if (line == "NoNewPrivs:\t1") nnp = true;
            if (line == "Seccomp:\t2") seccomp = true;
        }
        if (!(caps && nnp && seccomp)) return 33;
        DIR* input = opendir("/input"); if (!input) return 34;
        unsigned files = 0;
        while (dirent* entry = readdir(input)) {
            std::string name(entry->d_name);
            if (name == "." || name == "..") continue;
            if (name != "main" && name != "stdin.txt") return 35;
            ++files;
        }
        closedir(input); if (files != 2) return 36;
        for (const char* path : {"/input/writable", "/rootfs-writable", "/etc/hosts"}) {
            int fd = open(path, O_WRONLY | O_CREAT, 0600);
            if (fd >= 0) { close(fd); return 37; }
        }
        std::cout << "isolation=passed\n"; return 0;
    }
    return 99;
}
