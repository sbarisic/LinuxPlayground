#define _GNU_SOURCE

#include <errno.h>
#include <fcntl.h>
#include <linux/reboot.h>
#include <signal.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mount.h>
#include <sys/reboot.h>
#include <sys/stat.h>
#include <sys/sysmacros.h>
#include <sys/wait.h>
#include <unistd.h>

#define DEV_PATH "/dev"
#define CONSOLE_PATH "/dev/console"
#define SERIAL_PATH "/dev/ttyS0"
#define PROC_PATH "/proc"
#define SYS_PATH "/sys"
#define RUN_PATH "/run"
#define TMP_PATH "/tmp"
#define SERVICE_MANAGER_PATH "/system/ServiceManager"

static int serial_fd = -1;
static volatile sig_atomic_t shutdown_command = 0;

enum {
    SHUTDOWN_NONE = 0,
    SHUTDOWN_REBOOT = 1,
    SHUTDOWN_POWEROFF = 2
};

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
    ensure_directory(DEV_PATH, 0755);

    if (mknod(CONSOLE_PATH, S_IFCHR | 0600, makedev(5, 1)) != 0 && errno != EEXIST) {
        /* The kernel may still provide /dev/console after devtmpfs is mounted. */
    }

    if (mknod(SERIAL_PATH, S_IFCHR | 0600, makedev(4, 64)) != 0 && errno != EEXIST) {
        /* Serial logging is best-effort; VGA remains the primary console. */
    }

    int console_fd = open(CONSOLE_PATH, O_RDWR | O_CLOEXEC);
    if (console_fd < 0) {
        return;
    }

    serial_fd = open(SERIAL_PATH, O_WRONLY | O_CLOEXEC | O_NOCTTY);

    dup2(console_fd, STDIN_FILENO);
    dup2(console_fd, STDOUT_FILENO);
    dup2(console_fd, STDERR_FILENO);

    if (console_fd > STDERR_FILENO) {
        close(console_fd);
    }

    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
}

static void handle_shutdown_signal(int signal_number)
{
    if (signal_number == SIGUSR1) {
        shutdown_command = SHUTDOWN_REBOOT;
        return;
    }

    if (signal_number == SIGUSR2) {
        shutdown_command = SHUTDOWN_POWEROFF;
    }
}

static void setup_shutdown_signals(void)
{
    struct sigaction action;
    memset(&action, 0, sizeof(action));
    action.sa_handler = handle_shutdown_signal;
    sigemptyset(&action.sa_mask);

    if (sigaction(SIGUSR1, &action, NULL) != 0) {
        console_write("[init] sigaction SIGUSR1 failed: %s\n", strerror(errno));
    }

    if (sigaction(SIGUSR2, &action, NULL) != 0) {
        console_write("[init] sigaction SIGUSR2 failed: %s\n", strerror(errno));
    }
}

static void perform_shutdown_if_requested(void)
{
    int command = shutdown_command;
    if (command == SHUTDOWN_NONE) {
        return;
    }

    shutdown_command = SHUTDOWN_NONE;

    if (command == SHUTDOWN_POWEROFF) {
        console_write("[init] poweroff requested\n");
        sync();
        reboot(LINUX_REBOOT_CMD_POWER_OFF);
        console_write("[init] poweroff failed: %s\n", strerror(errno));
        return;
    }

    console_write("[init] reboot requested\n");
    sync();
    reboot(LINUX_REBOOT_CMD_RESTART);
    console_write("[init] reboot failed: %s\n", strerror(errno));
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

static void log_orphan_exit(pid_t pid, int status)
{
    if (WIFEXITED(status)) {
        console_write("[init] reaped child %d exited with status %d\n", pid, WEXITSTATUS(status));
        return;
    }

    if (WIFSIGNALED(status)) {
        console_write("[init] reaped child %d terminated by signal %d\n", pid, WTERMSIG(status));
        return;
    }

    console_write("[init] reaped child %d stopped with status %d\n", pid, status);
}

static pid_t start_service_manager(void)
{
    console_write("[init] starting %s\n", SERVICE_MANAGER_PATH);

    pid_t pid = fork();
    if (pid < 0) {
        console_write("[init] fork failed: %s\n", strerror(errno));
        return -1;
    }

    if (pid == 0) {
        execl(SERVICE_MANAGER_PATH, SERVICE_MANAGER_PATH, (char *)NULL);
        console_write("[init] exec %s failed: %s\n", SERVICE_MANAGER_PATH, strerror(errno));
        _exit(127);
    }

    return pid;
}

static void supervise_service_manager(void)
{
    pid_t service_manager_pid = -1;

    for (;;) {
        perform_shutdown_if_requested();

        if (service_manager_pid < 0) {
            service_manager_pid = start_service_manager();
        }

        if (service_manager_pid < 0) {
            sleep(1);
            continue;
        }

        int status = 0;
        pid_t waited = waitpid(-1, &status, 0);
        if (waited < 0 && errno == EINTR) {
            perform_shutdown_if_requested();
            continue;
        }

        if (waited < 0) {
            console_write("[init] waitpid failed: %s\n", strerror(errno));
            if (errno == ECHILD) {
                service_manager_pid = -1;
            }
            sleep(1);
            continue;
        }

        if (waited == service_manager_pid) {
            log_service_manager_exit(status);
            service_manager_pid = -1;
            console_write("[init] restarting ServiceManager in 1 second\n");
            sleep(1);
            continue;
        }

        log_orphan_exit(waited, status);
    }
}

int main(void)
{
    setup_console();
    setup_shutdown_signals();

    console_write("LinuxPlayground init starting\n");

    mount_filesystem("devtmpfs", "devtmpfs", DEV_PATH, "devtmpfs", MS_NOSUID);
    mount_filesystem("proc", "proc", PROC_PATH, "proc", MS_NOSUID | MS_NOEXEC | MS_NODEV);
    mount_filesystem("sysfs", "sysfs", SYS_PATH, "sysfs", MS_NOSUID | MS_NOEXEC | MS_NODEV);
    mount_filesystem("tmpfs", "tmpfs", RUN_PATH, "tmpfs", MS_NOSUID | MS_NODEV);
    mount_filesystem("tmpfs", "tmpfs", TMP_PATH, "tmpfs", MS_NOSUID | MS_NODEV);

    supervise_service_manager();

    return EXIT_SUCCESS;
}
