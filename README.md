# Unity-MultiWindow-Server
used to create more OpenGL windows, especial for Unity mods (maybe it's not the best solution)
## What a client should do
1. have a pair of named pipes(maybe sometimes we only needs one)
2. handle the connection process
3. have a piece of shared memory
4. send the shared memory's address to the server and start filling the shared memory(in RGBA32(well, Alpha is ignored by design))
......
5. when you no longer need this window, send CliendEnd and just throw the named pipes and shared memory away.