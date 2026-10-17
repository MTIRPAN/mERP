import type { OrgUnitTreeNode } from '@/shared/api/generated/model';

/** A unit of the organization tree endpoint with neutral values (PLAN H1b tests). */
export const unit = (
  id: string,
  parentId: string | null,
  overrides: Partial<OrgUnitTreeNode> = {},
): OrgUnitTreeNode => ({
  id,
  parentId,
  code: id.toUpperCase(),
  name: `Birim ${id}`,
  type: 'Department',
  managerPositionId: null,
  managerPositionCode: null,
  managerJobName: null,
  costCenterCode: null,
  branchId: null,
  validFrom: null,
  validTo: null,
  isActive: true,
  positionCount: 0,
  norm: 0,
  filled: 0,
  vacant: 0,
  ...overrides,
});
