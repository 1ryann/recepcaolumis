export const onlyDigits6 = (v: string) => v.replace(/\D/g, '').slice(0, 6)
export const isComplete6 = (v: string) => onlyDigits6(v).length === 6
