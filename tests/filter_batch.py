"""Two-pass search, source-mode equivalence, and managed activation retirement."""
import json
import sys


def register(h):
    h.record('exceptions/two-pass-search', lambda: run(h))


def run(h):
    executions = 0
    for folder, name in [('programs','FilterOrdering'),('bcl','FilterAsync')]:
        for debug in (False,True):
            d=h.OUT/'two-pass'/(name+('-debug' if debug else '-release'));d.mkdir(parents=True,exist_ok=True)
            pe=d/(name+'.dll');h.cli('emit-pe',h.ROOT/'tests'/folder/(name+'.cs'),'--out',pe,*(['--debug'] if debug else []))
            oracle=h.run('dotnet',pe)
            for mode in ('instruction','block'):
                for target,ext,host in [('js','mjs','node'),('py','py',sys.executable)]:
                    output=d/(mode+'.'+ext)
                    h.cli('compile',pe,'--target',target,'--out',output,'--dispatch',mode,*(['--bcl','portable'] if folder=='bcl' else []))
                    result=h.run(host,output)
                    assert result.stdout==oracle.stdout,(name,mode,target,result.stdout,oracle.stdout)
                    driver=d/('check-'+mode+'.'+ext)
                    if target=='js':
                        driver.write_text("import {main,runtimeInfo,setOutput} from './"+output.name+"';\nsetOutput(()=>{});for(let i=0;i<4;i++)main([]);const info=runtimeInfo();if(info.activeFrames!==0||info.exceptionPolicy!=='two-pass-managed-v1')throw new Error('activation leak');console.log('frames retired');")
                    else:
                        driver.write_text("import importlib.util\ns=importlib.util.spec_from_file_location('generated',"+repr(str(output))+ ")\nm=importlib.util.module_from_spec(s);s.loader.exec_module(m)\nm.set_output(lambda text:None)\nfor _ in range(4): m.main([])\nassert m.runtime_info()['activeFrames']==0\nassert not m.R.search_plans\nprint('frames retired')\n")
                    assert h.run(host,driver).stdout=='frames retired\n'
                    executions+=2
    return dict(targetExecutions=executions,searchBeforeUnwind=True,framesRetired=True)
