-- Checks whether a session is live. ARGV[4] is the session's id.
-- Returns {0} when the session is live, and {1} when it is not.
if live(ARGV[4]) then
  return finish({0})
end
return finish({1})
