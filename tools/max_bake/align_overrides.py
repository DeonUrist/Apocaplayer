# head: landmark fit (nose tip, eye line, mouth, chin, back of skull) instead of the bounding box -
# the game man's stylised head is ~1.4x larger than Max's, with the face higher on the skull
hk=names.index('Head')
sc=np.array([0.85,0.72,0.78]); maps[hk,0]=sc; maps[hk,1]=np.array([0.004,0.562,0.101])-np.array([0.0,0.66,0.142])*sc
