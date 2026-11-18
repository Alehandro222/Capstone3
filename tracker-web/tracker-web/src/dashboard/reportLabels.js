import { AGE_GROUPS, SECTORS, SERVICES, SEXES } from '../registration.js'
import { END_REASON_LABEL, SOURCE_LABEL } from './format.js'

const label = (options) => Object.fromEntries(options.map((o) => [o.value, o.label]))

// The form's wording for each answer in a report.
export const LABELS = {
  age: label(AGE_GROUPS),
  sex: label(SEXES),
  sector: label(SECTORS),
  service: label(SERVICES),
  source: SOURCE_LABEL,
  end: END_REASON_LABEL,
}

export const fmt = (n) => n.toLocaleString('en-PH', { maximumFractionDigits: 1 })
