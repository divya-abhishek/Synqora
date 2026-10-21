"""Pass an ephemeral pairing secret through stdin into app-private test storage."""
import os
import pathlib
import subprocess

adb = os.environ['SYNQORA_TEST_ADB']
subprocess.run([adb, 'shell', 'run-as', 'com.synqora', 'mkdir', '-p', 'files'], check=True)
subprocess.run([adb, 'shell', "run-as com.synqora sh -c 'cat > files/test-pairing.txt'"], input=pathlib.Path(os.environ['SYNQORA_TEST_INVITE']).read_bytes(), check=True)
