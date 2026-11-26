-- Ends a session and removes up to 64 of what is bound to it; the rest goes with later scripts. ARGV[4] is the session's id.
-- Returns {0}.
endSession(ARGV[4], 64)
return finish({0})
