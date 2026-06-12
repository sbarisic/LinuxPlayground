#define _GNU_SOURCE

#include <errno.h>
#include <fcntl.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mount.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <unistd.h>

static void console_write(const char *format, ...)
{
    va_list args;
    va_start(args, format);
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

    int console_fd = open("/dev/console", O_RDWR | O_CLOEXEC);
    if (console_fd < 0) {
        return;
    }

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

int main(void)
{
    setup_console();

    console_write("LinuxPlayground init starting\n");

    mount_filesystem("devtmpfs", "devtmpfs", "/dev", "devtmpfs", MS_NOSUID);
    mount_filesystem("proc", "proc", "/proc", "proc", MS_NOSUID | MS_NOEXEC | MS_NODEV);
    mount_filesystem("sysfs", "sysfs", "/sys", "sysfs", MS_NOSUID | MS_NOEXEC | MS_NODEV);
    mount_filesystem("tmpfs", "tmpfs", "/run", "tmpfs", MS_NOSUID | MS_NODEV);
    mount_filesystem("tmpfs", "tmpfs", "/tmp", "tmpfs", MS_NOSUID | MS_NODEV);

    console_write("[init] init idle\n");

    for (;;) {
        pause();
    }

    return EXIT_SUCCESS;
}
