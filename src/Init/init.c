#define _GNU_SOURCE

#include <errno.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <sys/wait.h>
#include <unistd.h>

#define SERVICE_MANAGER_PATH "/system/ServiceManager"

static int serial_fd = -1;

static void console_write(const char *format, ...)
{
    va_list args;
    va_start(args, format);

    if (serial_fd >= 0) {
        va_list serial_args;
        va_copy(serial_args, args);
        vdprintf(serial_fd, format, serial_args);
        va_end(serial_args);
    }

    vprintf(format, args);
    va_end(args);
    fflush(stdout);
}

static void ensure_directory(const char *path, mode_t mode)
{
    if (mkdir(path, mode) == 0 || errno == EEXIST) {
        return;
    }

    console_write("[init] mkdir %s failed: %s\n", path, strerror(errno));
}

static void setup_console(void)
{
    ensure_directory("/dev", 0755);

    if (mknod("/dev/console", S_IFCHR | 0600, makedev(5, 1)) != 0 && errno != EEXIST) {
        /* The kernel may still provide /dev/console after devtmpfs is mounted. */
    }

    if (mknod("/dev/ttyS0", S_IFCHR | 0600, makedev(4, 64)) != 0 && errno != EEXIST) {
        /* Serial logging is best-effort; VGA remains the primary console. */
    }

    int console_fd = open("/dev/console", O_RDWR | O_CLOEXEC);
    if (console_fd < 0) {
        return;
    }

    serial_fd = open("/dev/ttyS0", O_WRONLY | O_CLOEXEC | O_NOCTTY);

    dup2(console_fd, STDIN_FILENO);
    dup2(console_fd, STDOUT_FILENO);
    dup2(console_fd, STDERR_FILENO);

    if (console_fd > STDERR_FILENO) {
        close(console_fd);
    }

    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
}

static void mount_filesystem(const char *label, const char *source, const char *target, const char *type, unsigned long flags)
{
    ensure_directory(target, 0755);

    if (mount(source, target, type, flags, "") == 0) {
        console_write("[init] mounted %s on %s\n", label, target);
        return;
    }

    if (errno == EBUSY) {
        console_write("[init] %s already mounted on %s\n", label, target);
        return;
    }

    console_write("[init] failed to mount %s on %s: %s\n", label, target, strerror(errno));
}

static void log_service_manager_exit(int status)
{
    if (WIFEXITED(status)) {
        console_write("[init] ServiceManager exited with status %d\n", WEXITSTATUS(status));
        return;
    }

    if (WIFSIGNALED(status)) {
        console_write("[init] ServiceManager terminated by signal %d\n", WTERMSIG(status));
        return;
    }

    console_write("[init] ServiceManager stopped with status %d\n", status);
}

static void supervise_service_manager(void)
{
    for (;;) {
        console_write("[init] starting %s\n", SERVICE_MANAGER_PATH);

        pid_t pid = fork();
        if (pid < 0) {
            console_write("[init] fork failed: %s\n", strerror(errno));
            sleep(1);
            continue;
        }

        if (pid == 0) {
            execl(SERVICE_MANAGER_PATH, SERVICE_MANAGER_PATH, (char *)NULL);
            console_write("[init] exec %s failed: %s\n", SERVICE_MANAGER_PATH, strerror(errno));
            _exit(127);
        }

        bool have_status = false;
        int status = 0;
        for (;;) {
            pid_t waited = waitpid(pid, &status, 0);
            if (waited == pid) {
                have_status = true;
                break;
            }

            if (waited < 0 && errno == EINTR) {
                continue;
            }

            console_write("[init] waitpid failed: %s\n", strerror(errno));
            break;
        }

        if (have_status) {
            log_service_manager_exit(status);
        }
        console_write("[init] restarting ServiceManager in 1 second\n");
        sleep(1);
    }
}

int main(void)
{
    setup_console();

    console_write("LinuxPlayground init starting\n");

    mount_filesystem("devtmpfs", "devtmpfs", "/dev", "devtmpfs", MS_NOSUID);
    mount_filesystem("proc", "proc", "/proc", "proc", MS_NOSUID | MS_NOEXEC | MS_NODEV);
    mount_filesystem("sysfs", "sysfs", "/sys", "sysfs", MS_NOSUID | MS_NOEXEC | MS_NODEV);
    mount_filesystem("tmpfs", "tmpfs", "/run", "tmpfs", MS_NOSUID | MS_NODEV);
    mount_filesystem("tmpfs", "tmpfs", "/tmp", "tmpfs", MS_NOSUID | MS_NODEV);

    supervise_service_manager();

    return EXIT_SUCCESS;
}
