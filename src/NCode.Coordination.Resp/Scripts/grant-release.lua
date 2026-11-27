-- Releases a grant. ARGV[4] is the grant's field and ARGV[5] its owner token.
-- Returns {0} when it released the grant, and {1} when the grant is absent or another owner holds it.
local h = holder(ARGV[4])
if not h or h[1] ~= ARGV[5] then
  return finish({1})
end
drop(ARGV[4])
return finish({0})
