import test from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';

const bundle = await build({
  entryPoints: [fileURLToPath(new URL('../src/components/VoiceSettings/VoiceSettings.tsx', import.meta.url))],
  bundle: true, write: false, format: 'esm', jsx: 'automatic',
  plugins: [{ name: 'input-panel-fixture', setup(builder) {
    builder.onResolve({ filter: /.*/ }, args => args.kind === 'entry-point' ? undefined : { path: args.path, namespace: 'fixture' });
    builder.onLoad({ filter: /.*/, namespace: 'fixture' }, ({ path }) => ({ contents:
      path === 'react/jsx-runtime' ? 'export const jsx=(type,props)=>({type,props});export const jsxs=jsx;export const Fragment="Fragment";'
      : path === 'react' ? 'const h=globalThis.inputSwitchFixture;export const useState=v=>h.state(v),useRef=v=>h.ref(v),useEffect=()=>{};export default {};'
      : path === 'antd' ? 'export const Modal="Modal",Select="Select",Button="Button",Typography={Text:"Text"},Space="Space",Progress="Progress",message={success:()=>{},error:()=>{}};'
      : path === 'react-i18next' ? 'export const useTranslation=()=>{};'
      : path.endsWith('/i18n') ? 'export const tl=zh=>zh;'
      : path.endsWith('/audioDevices') ? 'export const audioDevices={getInputDeviceId:()=>globalThis.inputSwitchFixture.selected,setInputDeviceId:id=>globalThis.inputSwitchFixture.selected=id};'
      : path.endsWith('/services') ? 'export const webrtcClient={refreshMicrophoneProcessing:()=>globalThis.inputSwitchFixture.refresh()};'
      : path.endsWith('/microphoneLevel') ? 'export const microphoneLevelPercent=x=>x,pcmRms=x=>x;'
      : 'export const microphoneDevices=()=>[],nativeMicrophoneLevel=()=>0,openMicrophone=()=>{},resumeAudioContext=()=>{};'
    }));
  } }],
});

test('real input panel rebinds an active call immediately and rolls back failed input changes', async () => {
  const states = [], refs = [], refreshes = [];
  let stateIndex = 0, refIndex = 0, rejectNext = false;
  globalThis.inputSwitchFixture = {
    selected: 'old',
    state(initial) { const key = stateIndex++; if (!(key in states)) states[key] = initial; return [states[key], value => states[key] = value]; },
    ref(initial) { const key = refIndex++; return refs[key] ??= { current: initial }; },
    async refresh() { refreshes.push(this.selected); if (rejectNext) { rejectNext = false; throw new Error('unavailable'); } },
  };
  try {
    const { VoiceDevicePanel } = await import(`data:text/javascript,${encodeURIComponent(bundle.outputFiles[0].text)}`);
    function find(tree) {
      if (Array.isArray(tree)) return tree.flatMap(find);
      return tree && typeof tree === 'object' ? [tree, ...find(tree.props?.children)] : [];
    }
    stateIndex = refIndex = 0;
    const selector = find(VoiceDevicePanel({})).find(node => node.type === 'Select');
    await selector.props.onChange('working');
    assert.equal(globalThis.inputSwitchFixture.selected, 'working');
    assert.deepEqual(refreshes, ['working']);
    rejectNext = true;
    await selector.props.onChange('missing');
    assert.equal(globalThis.inputSwitchFixture.selected, 'working');
    assert.deepEqual(refreshes, ['working', 'missing', 'working']);
  } finally { delete globalThis.inputSwitchFixture; }
});
