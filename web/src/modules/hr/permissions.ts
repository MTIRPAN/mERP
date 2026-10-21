/** Mirrors Merp.Modules.HR.Application.HrPermissions. */
export const hrPermissions = {
  orgRead: 'hr.org.read',
  orgManage: 'hr.org.manage',
  employeesRead: 'hr.employees.read',
  employeesCreate: 'hr.employees.create',
  employeesUpdate: 'hr.employees.update',
  employeesTerminate: 'hr.employees.terminate',
  employeesExport: 'hr.employees.export',
  sensitiveRead: 'hr.employees.sensitive.read',
  sensitiveUpdate: 'hr.employees.sensitive.update',
  documentsRead: 'hr.employees.documents.read',
  documentsManage: 'hr.employees.documents.manage',
} as const;
