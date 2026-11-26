-- Checks whether a grant is held. ARGV[4] is the grant's field.
-- Returns {0} when the grant is held, and {1} when it is not.
if holder(ARGV[4]) then
  return finish({0})
end
return finish({1})
