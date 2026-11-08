// The API sends times in UTC ending in "Z". Older data may lack the "Z";
// add it back so the browser never reads the time as local time.
export function parseUtc(iso) {
  return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`)
}
