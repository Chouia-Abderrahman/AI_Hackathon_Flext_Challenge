import type { IkonUiModuleLoader, IkonUiRegistry } from '@ikonai/sdk-react-ui';
import { createRoofHeroResolver } from './components/roof-hero';

export const loadRoofHeroModule: IkonUiModuleLoader = () => [createRoofHeroResolver()];

export function registerRoofHeroModule(registry: IkonUiRegistry): void {
  registry.registerModule('roof-hero', loadRoofHeroModule);
}
