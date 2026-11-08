// The registration form's choices, matching the DTC Baguio digital login form.
// `value` is what the API expects; `label` is what people see.
// (Shared by the staff walk-in form and, later, the visitor phone app.)

export const AGE_GROUPS = [
  { value: 'Age17AndUnder', label: '17 years old & under' },
  { value: 'Age18To30', label: '18 – 30 years old' },
  { value: 'Age31To59', label: '31 – 59 years old' },
  { value: 'Age60AndAbove', label: '60 years old & above' },
]

export const SEXES = [
  { value: 'Male', label: 'Male' },
  { value: 'Female', label: 'Female' },
]

export const SECTORS = [
  { value: 'Nga', label: 'NGA' },
  { value: 'Lgu', label: 'LGU' },
  { value: 'Academe', label: 'Academe' },
  { value: 'Youth', label: 'Youth' },
  { value: 'SeniorCitizen', label: 'Senior Citizen' },
  { value: 'Msme', label: 'MSMEs' },
  { value: 'IndigenousPeople', label: 'Indigenous People' },
  { value: 'Pwd', label: 'PWD' },
  { value: 'InformalSector', label: 'Informal Sector' },
  { value: 'Others', label: 'Others' },
]

export const SERVICES = [
  { value: 'Connectivity', label: 'Connectivity' },
  { value: 'IctEquipment', label: 'Use of ICT Equipment' },
  { value: 'CoWorkingSpace', label: 'Co-Working Space' },
  { value: 'EgovServices', label: 'eGov Services' },
  { value: 'Others', label: 'Others' },
]

// DICT-CAR's privacy statement, shown on every registration form.
export const PRIVACY_STATEMENT =
  'The participants are assured that the personal data and other sensitive data entrusted to the ' +
  'Department of Information and Communications Technology - Cordillera Administrative Region ' +
  '(DICT-CAR) shall be used with due diligence and prudence, for the sole purpose of gathering ' +
  'registrations. As such, upon accessing the DICT-CAR registration form, you acknowledge and agree ' +
  'that the information may be used and disclosed by the DICT-CAR in accordance with any legal and ' +
  'regulatory standards and in compliance with the "Data Privacy Act of 2012".'

export const PRIVACY_CONSENT =
  'By accessing and submitting this form, you agree to the above privacy statement and consent to ' +
  'the use and disclosure of your information as described.'

export const emptyRegistration = () => ({
  firstName: '',
  lastName: '',
  ageGroup: '',
  sex: '',
  sectors: [],
  services: [],
})

// Returns the first problem to show, or '' when everything required is filled in.
export function registrationProblem(r) {
  if (!r.firstName.trim()) return 'Enter the first name.'
  if (!r.lastName.trim()) return 'Enter the last name.'
  if (!r.ageGroup) return 'Choose an age group.'
  if (!r.sex) return 'Choose a sex.'
  if (r.sectors.length === 0) return 'Select at least one sector.'
  if (r.services.length === 0) return 'Select at least one purpose or service.'
  return ''
}
