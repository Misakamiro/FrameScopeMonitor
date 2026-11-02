import React from "react";
import {traceStartup} from '@/lib/startup-trace';

type Props = { children: React.ReactNode };
type State = { error: Error | null };

/**
 * Last-resort error boundary around the whole app. Without it a render error
 * unmounts the entire tree and the window shows nothing but a black surface.
 */
export class RootErrorBoundary extends React.Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    traceStartup('frontend-failed');
    console.error("RootErrorBoundary caught:", error, info.componentStack);
  }

  render() {
    if (this.state.error) {
      return <div role="alert" style={{minHeight:'100vh',display:'grid',placeContent:'center',background:'#15171d',color:'#f4f5f7',padding:32,gap:16,fontFamily:'system-ui'}}>
        <h1>界面出现了问题</h1><p>设置与监测数据已保留，可以重新加载界面。</p>
        <button style={{padding:'10px 18px',borderRadius:8}} onClick={()=>window.location.reload()}>重新加载界面</button>
      </div>;
    }
    return this.props.children;
  }
}
