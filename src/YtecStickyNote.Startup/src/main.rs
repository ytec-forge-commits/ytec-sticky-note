#![windows_subsystem = "windows"]

use std::env;
use std::ffi::OsString;
use std::fs::{self, File, OpenOptions};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::thread;
use std::time::{Duration, Instant};

const DEFAULT_TIMEOUT_SECONDS: u64 = 600;
const DEFAULT_POLL_MILLISECONDS: u64 = 2_000;
const STABILIZATION_MILLISECONDS: u64 = 1_500;
const REQUIRED_SIBLINGS: &[&str] = &[
    "YTEC-Sticky-Note.dll",
    "YTEC-Sticky-Note.deps.json",
    "YTEC-Sticky-Note.runtimeconfig.json",
    "hostfxr.dll",
    "hostpolicy.dll",
    "coreclr.dll",
    "PresentationFramework.dll",
];

struct Options {
    config_path: PathBuf,
    timeout: Duration,
    poll_interval: Duration,
}

#[derive(Debug, PartialEq)]
struct StartupTarget {
    executable: PathBuf,
    data_file: Option<PathBuf>,
}

fn main() {
    let Some(options) = parse_options(env::args_os().skip(1)) else {
        return;
    };

    if let Some(target) = wait_for_target(
        &options.config_path,
        options.timeout,
        options.poll_interval,
        Duration::from_millis(STABILIZATION_MILLISECONDS),
    ) {
        let _ = launch_target(&target.executable);
    }
}

fn wait_for_target(
    config_path: &Path,
    timeout: Duration,
    poll_interval: Duration,
    stabilization: Duration,
) -> Option<StartupTarget> {
    let started_at = Instant::now();
    while started_at.elapsed() <= timeout {
        if let Some(target) = read_target(config_path)
            && required_files_available(&target)
        {
            thread::sleep(stabilization);
            if required_files_available(&target) {
                return Some(target);
            }
        }

        thread::sleep(poll_interval);
    }

    None
}

fn parse_options<I>(arguments: I) -> Option<Options>
where
    I: IntoIterator<Item = OsString>,
{
    let mut arguments = arguments.into_iter();
    let mut config_path = None;
    let mut timeout_seconds = DEFAULT_TIMEOUT_SECONDS;
    let mut poll_milliseconds = DEFAULT_POLL_MILLISECONDS;

    while let Some(argument) = arguments.next() {
        match argument.to_string_lossy().as_ref() {
            "--config" => config_path = arguments.next().map(PathBuf::from),
            "--timeout-seconds" => {
                timeout_seconds = arguments.next()?.to_string_lossy().parse().ok()?;
            }
            "--poll-milliseconds" => {
                poll_milliseconds = arguments.next()?.to_string_lossy().parse().ok()?;
            }
            _ => return None,
        }
    }

    Some(Options {
        config_path: config_path?,
        timeout: Duration::from_secs(timeout_seconds),
        poll_interval: Duration::from_millis(poll_milliseconds.max(50)),
    })
}

fn read_target(config_path: &Path) -> Option<StartupTarget> {
    let contents = fs::read_to_string(config_path).ok()?;
    let mut lines = contents.lines();
    let executable = lines.next()?.trim().trim_start_matches('\u{feff}');
    if executable.is_empty() {
        return None;
    }

    let data_file = lines.find_map(|line| {
        line.trim()
            .strip_prefix("data=")
            .filter(|path| !path.is_empty())
            .map(PathBuf::from)
    });
    Some(StartupTarget {
        executable: PathBuf::from(executable),
        data_file,
    })
}

fn required_files_available(target: &StartupTarget) -> bool {
    let Some(directory) = target.executable.parent() else {
        return false;
    };

    can_open(&target.executable)
        && REQUIRED_SIBLINGS
            .iter()
            .map(|file_name| directory.join(file_name))
            .all(|path| can_open(&path))
        && target
            .data_file
            .as_ref()
            .is_none_or(|path| can_open(path) && path.parent().is_some_and(directory_is_writable))
        && directory_is_writable(directory)
}

fn can_open(path: &Path) -> bool {
    path.is_file() && File::open(path).is_ok()
}

fn directory_is_writable(directory: &Path) -> bool {
    if !directory.is_dir() {
        return false;
    }

    let probe = directory.join(format!(
        ".ytec-sticky-note-startup-probe-{}.tmp",
        std::process::id()
    ));
    let result = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&probe)
        .and_then(|file| {
            file.sync_all()?;
            drop(file);
            fs::remove_file(&probe)
        });

    if result.is_err() {
        let _ = fs::remove_file(probe);
    }
    result.is_ok()
}

fn launch_target(target: &Path) -> bool {
    let Some(directory) = target.parent() else {
        return false;
    };

    Command::new(target).current_dir(directory).spawn().is_ok()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn readiness_requires_the_executable_and_all_runtime_files() {
        let directory = temporary_directory("readiness");
        let executable = directory.join("YTEC-Sticky-Note.exe");
        let target = StartupTarget {
            executable: executable.clone(),
            data_file: None,
        };
        fs::write(&executable, b"test").unwrap();
        assert!(!required_files_available(&target));

        for file_name in REQUIRED_SIBLINGS {
            fs::write(directory.join(file_name), b"test").unwrap();
        }
        assert!(required_files_available(&target));

        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn config_reader_trims_whitespace_and_utf8_bom() {
        let directory = temporary_directory("config");
        let config = directory.join("startup-target.txt");
        fs::write(
            &config,
            "\u{feff}D:\\Drive\\YTEC-Sticky-Note.exe\r\ndata=D:\\Drive\\data\\sticky-note.json\r\n",
        )
        .unwrap();

        assert_eq!(
            read_target(&config),
            Some(StartupTarget {
                executable: PathBuf::from("D:\\Drive\\YTEC-Sticky-Note.exe"),
                data_file: Some(PathBuf::from("D:\\Drive\\data\\sticky-note.json")),
            })
        );

        fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn waits_until_a_delayed_drive_becomes_ready() {
        let directory = temporary_directory("delayed-drive");
        let executable = directory.join("YTEC-Sticky-Note.exe");
        let config = directory.join("startup-target.txt");
        fs::write(&config, executable.to_string_lossy().as_bytes()).unwrap();

        let (ready_sender, ready_receiver) = mpsc::channel();
        let delayed_directory = directory.clone();
        let delayed_target = executable.clone();
        let writer = thread::spawn(move || {
            thread::sleep(Duration::from_millis(80));
            fs::write(delayed_target, b"test").unwrap();
            for file_name in REQUIRED_SIBLINGS {
                fs::write(delayed_directory.join(file_name), b"test").unwrap();
            }
            ready_sender.send(()).unwrap();
        });

        let found = wait_for_target(
            &config,
            Duration::from_secs(1),
            Duration::from_millis(20),
            Duration::from_millis(20),
        );
        ready_receiver.recv().unwrap();
        writer.join().unwrap();

        assert_eq!(
            found,
            Some(StartupTarget {
                executable,
                data_file: None,
            })
        );
        fs::remove_dir_all(directory).unwrap();
    }

    fn temporary_directory(label: &str) -> PathBuf {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let directory = env::temp_dir().join(format!(
            "ytec-sticky-note-startup-{label}-{}-{nonce}",
            std::process::id()
        ));
        fs::create_dir_all(&directory).unwrap();
        directory
    }
}
